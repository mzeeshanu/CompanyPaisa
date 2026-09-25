// Page titles, the same as the server writes them (SitePages.Titled in the API): search engines read the title after the
// app has run, so the two must agree.

/** The home page's title. */
export const HOME_TITLE = 'CompanyPaisa — public companies near you and what they pay';

const BRAND = ' · CompanyPaisa';
const MAX_TITLE_LENGTH = 65;

/** The page's own words, then " · CompanyPaisa" when it still fits in the ~65 characters search results show. */
export const titled = (words: string) => (words.length + BRAND.length <= MAX_TITLE_LENGTH ? words + BRAND : words);
