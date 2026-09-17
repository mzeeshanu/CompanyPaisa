interface Props { onAccept: () => void; onDecline: () => void }

export function ConsentBanner({ onAccept, onDecline }: Props) {
  return (
    <div className="consent pane" role="dialog" aria-label="Cookie preferences">
      <p><b>Remember your view and theme?</b> We'd store one small preference cookie on this device — no tracking cookies, no ads, nothing shared.</p>
      <div className="btns">
        <button className="ghost" onClick={onDecline}>Just this visit</button>
        <button className="primary" onClick={onAccept}>Remember my choices</button>
      </div>
    </div>
  );
}
