"""EdgarTools cross-check, step 1: read each SEC company's latest annual report (10-K, 20-F or 40-F) with EdgarTools and
write the income statement's headline revenue and net income per year to data/cache/edgar/<company id>.json.

The SEC import takes a report's top line where it can prove it right, and the validator (--validate) lists whatever
still differs in data/edgar-crosscheck.md (docs/edgartools-plan.md).

A company is read again only when it has filed a newer annual report (or EdgarTools was upgraded), so after the first
run a monthly refresh reads only the companies that published a report that month.

Run through tools/edgar/run.ps1, which sets up the pinned Python environment:
    powershell -ExecutionPolicy Bypass -File tools\\edgar\\run.ps1                      # every SEC company
    powershell -ExecutionPolicy Bypass -File tools\\edgar\\run.ps1 --tickers GS,AAPL    # a few, for a quick look
"""
from __future__ import annotations

import argparse
import datetime as dt
import json
import os
import re
import sqlite3
import sys
import time
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
ANNUAL_FORMS = ("10-K", "10-KT", "20-F", "40-F")
SCHEMA = 2  # raise when the JSON gains a field, so every company is read again
TOTAL_LABEL = re.compile(r"^total\b.*\b(revenues?|sales)\b", re.I)
NOT_A_TOTAL = re.compile(r"\b(cost|costs|expenses?)\b", re.I)
FY_COLUMN = re.compile(r"^(\d{4}-\d{2}-\d{2}) \(FY\)$")


def sec_identity(settings: Path) -> str:
    """The importer's Sec:UserAgent (name and contact email), so the SEC sees one identity from this PC."""
    def find(node):
        if isinstance(node, dict):
            sec = node.get("Sec")
            if isinstance(sec, dict) and sec.get("UserAgent"):
                return sec["UserAgent"]
            for value in node.values():
                if found := find(value):
                    return found
        return None

    identity = find(json.loads(settings.read_text(encoding="utf-8-sig"))) if settings.exists() else None
    if not identity:
        sys.exit(f"No Sec:UserAgent in {settings}: the SEC requires a name and contact email on every request.")
    return identity


def only_talk_to_the_sec() -> None:
    """The identity holds a contact email approved for sec.gov only, and EdgarTools has helpers that reach other hosts
    (ticker icons on GitHub): refuse every request, redirects included, that isn't HTTPS to sec.gov."""
    import httpx

    def check(request) -> None:
        host = request.url.host.lower()
        if request.url.scheme != "https" or not (host == "sec.gov" or host.endswith(".sec.gov")):
            raise RuntimeError(f"Blocked a request to {host}: the SEC identity is only sent to sec.gov")

    send, send_async = httpx.Client._send_single_request, httpx.AsyncClient._send_single_request

    def guarded(self, request):
        check(request)
        return send(self, request)

    async def guarded_async(self, request):
        check(request)
        return await send_async(self, request)

    httpx.Client._send_single_request = guarded
    httpx.AsyncClient._send_single_request = guarded_async


def sec_companies(db: Path, only: set[str] | None) -> list[tuple[str, int]]:
    """(company id, CIK) for every SEC company. The CIK comes from the SEC filing its newest figures cite."""
    con = sqlite3.connect(f"file:{db}?mode=ro", uri=True)
    # SQLite takes the bare f.url from the row holding max(fiscal_year).
    rows = con.execute("""
        select c.company_id, f.url, max(x.fiscal_year)
        from companies c
        join financials x on x.company_id = c.company_id and x.market = c.market
        join filings f on f.id = x.filing_id
        where c.market = 'sec'
        group by c.company_id""").fetchall()
    con.close()
    out = []
    for company_id, url, _ in rows:
        m = re.search(r"(?:~sec/|/edgar/data/)(\d+)/", url or "")
        if m and (only is None or company_id.upper() in only):
            out.append((company_id, int(m.group(1))))
    return sorted(out)


def latest_annual_report(company):
    """The newest unamended 10-K, 20-F or 40-F (a company that moved between them is read from its newest)."""
    found = [f for form in ANNUAL_FORMS
             if (f := company.get_filings(form=form, amendments=False, trigger_full_load=False).latest()) is not None]
    return max(found, key=lambda f: str(f.filing_date)) if found else None


def top_line(lines, column, revenue) -> dict:
    """The printed label and concept of the revenue line, and the largest line the statement calls a revenue total:
    what the importer needs to tell a headline from a sub-line."""
    line, largest = None, None
    for row in lines:
        try:
            value = float(row[column])
        except (TypeError, ValueError):
            continue
        if value != value:  # an empty cell
            continue
        label = str(row["label"]).strip()
        if line is None and revenue is not None and value == revenue:
            line = row
        if TOTAL_LABEL.search(label) and not NOT_A_TOTAL.search(label):
            largest = value if largest is None else max(largest, value)
    return {"label": None if line is None else str(line["label"]).strip(),
            "concept": None if line is None else str(line["concept"]).split("_", 1)[-1],
            "largest_total": None if largest is None else round(largest)}


def read_statement(filing) -> list[dict]:
    """Headline revenue and net income for each year the report's income statement shows (usually three)."""
    from edgar.financials import Financials

    financials = Financials.extract(filing)
    if financials is None:
        return []
    statement = financials.income_statement()
    if statement is None:
        return []
    table = statement.to_dataframe()
    columns = [(m.group(1), c) for c in table.columns if (m := FY_COLUMN.match(str(c)))]
    ends = [end for end, _ in columns]
    lines = [r for _, r in table.iterrows() if not (r.get("abstract") or r.get("dimension") or r.get("is_breakdown"))]
    periods = []
    for offset, end in enumerate(ends):
        revenue, net_income = financials.get_revenue(offset), financials.get_net_income(offset)
        if revenue is not None or net_income is not None:
            periods.append({"end": end,
                            **top_line(lines, columns[offset][1], revenue),
                            "revenue": None if revenue is None else round(revenue),
                            "net_income": None if net_income is None else round(net_income)})
    return periods


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--db", type=Path, default=REPO / "data" / "companypaisa.db")
    parser.add_argument("--out", type=Path, default=REPO / "data" / "cache" / "edgar")
    parser.add_argument("--settings", type=Path, default=REPO / "tools" / "CompanyPaisa.Importer" / "appsettings.Local.json")
    parser.add_argument("--tickers", help="comma-separated company ids, for a quick look at a few")
    parser.add_argument("--limit", type=int, help="stop after this many companies")
    parser.add_argument("--force", action="store_true", help="read every company again, even if its report hasn't changed")
    args = parser.parse_args()

    identity = sec_identity(args.settings)
    os.environ["EDGAR_IDENTITY"] = identity
    # Half the SEC's 10 requests a second: the importer and EdgarTools never run at the same time, but stay polite.
    os.environ.setdefault("EDGAR_RATE_LIMIT_PER_SEC", "5")
    # EdgarTools' own download cache sits with the importer's (data/cache, not committed).
    os.environ.setdefault("EDGAR_LOCAL_DATA_DIR", str(REPO / "data" / "cache" / "edgartools"))
    only_talk_to_the_sec()
    import edgar
    from edgar import Company, set_identity
    set_identity(identity)

    only = {t.strip().upper() for t in args.tickers.split(",")} if args.tickers else None
    companies = sec_companies(args.db, only)[: args.limit]
    args.out.mkdir(parents=True, exist_ok=True)
    counts = {"read": 0, "unchanged": 0, "no report": 0, "failed": 0}
    started = time.monotonic()

    for i, (company_id, cik) in enumerate(companies, 1):
        path = args.out / f"{company_id.upper()}.json"
        previous = json.loads(path.read_text(encoding="utf-8")) if path.exists() else {}
        record = {"schema": SCHEMA, "company_id": company_id, "cik": cik, "edgartools": edgar.__version__,
                  "checked": dt.datetime.now(dt.timezone.utc).isoformat(timespec="seconds")}
        try:
            filing = latest_annual_report(Company(cik))
            if filing is None:
                record["status"] = "no report"
                record["periods"] = []
            else:
                record.update(form=filing.form, accession=filing.accession_no, filed=str(filing.filing_date))
                if (not args.force and previous.get("accession") == filing.accession_no
                        and previous.get("edgartools") == edgar.__version__ and previous.get("status") == "ok" and previous.get("schema") == SCHEMA):
                    counts["unchanged"] += 1
                    continue
                record["periods"] = read_statement(filing)
                record["status"] = "ok" if record["periods"] else "no figures"
        except Exception as e:  # noqa: BLE001 - one company's bad filing must not stop the rest
            if previous.get("status") == "ok":  # keep the last good reading rather than lose it to one bad request
                counts["failed"] += 1
                continue
            record["status"] = "failed"
            record["error"] = f"{type(e).__name__}: {e}"[:400]
            record.setdefault("periods", [])
        counts["failed" if record["status"] == "failed" else "no report" if record["status"] == "no report" else "read"] += 1
        path.write_text(json.dumps(record, indent=1), encoding="utf-8")
        if i % 100 == 0 or only:
            rate = i / max(time.monotonic() - started, 1)
            print(f"{i}/{len(companies)}  {company_id}: {record['status']}  ({rate:.1f} companies/s)", flush=True)

    if not only and not args.limit and companies:
        # A full run owns the directory: files for companies that left, or changed ticker, would shadow the new ones.
        current = {f"{company_id.upper()}.json" for company_id, _ in companies}
        for path in args.out.glob("*.json"):
            if path.name not in current:
                path.unlink()

    print(f"Done: {len(companies)} companies in {(time.monotonic() - started) / 60:.0f} min - "
          + ", ".join(f"{v} {k}" for k, v in counts.items()), flush=True)
    # A run where nothing could be read means something is wrong (the SEC unreachable, an EdgarTools break).
    return 1 if companies and counts["failed"] == len(companies) else 0


if __name__ == "__main__":
    sys.exit(main())
