import type { Config } from 'tailwindcss';

/**
 * Newsprint, not app chrome.
 *
 * The product's premise is that it replaces attention-farming feeds, so the
 * palette is the one a printed page has: warm paper, near-black ink, hairline
 * rules, and exactly one spot colour. Saturated colour is reserved for things
 * that carry real meaning — trust, hype, urgency — and for the press red that
 * marks what is interactive.
 *
 * The old ramp was a cool blue-grey (#f6f7f9 → #0f1521) with an electric blue
 * accent (#1c48f5). Both read as software. Under a warm ink the same greys go
 * grey-blue and the page looks like a screenshot of an app; these values are
 * tuned against #faf7f1 paper instead.
 */
const config: Config = {
  content: ['./src/**/*.{ts,tsx}'],
  darkMode: 'class',
  theme: {
    extend: {
      colors: {
        /* Paper and ink. Contrast ratios in the comments are against ink-50. */
        ink: {
          50: '#faf7f1',
          100: '#f2ede4',
          200: '#e2dcd0',
          300: '#c9c1b2',
          400: '#8a827a',
          500: '#6f675f',
          600: '#57504a',
          700: '#3b3631',
          800: '#26231f',
          900: '#171310',
          950: '#12110f',
        },
        /*
         * The spot colour. A press keeps one ink besides black because a second
         * one costs another pass, and that scarcity is what makes it mean
         * something — here it marks the interactive and nothing else.
         *
         * Kept under the name `focus` on purpose: roughly forty call sites use
         * focus-600 / focus-400 already, and renaming the token would have been
         * a rename commit pretending to be a design commit.
         */
        focus: {
          50: '#fdf2f0',
          100: '#fadfda',
          200: '#f2b8ae',
          300: '#e07a6d',
          400: '#d15a4a',
          500: '#c0392c',
          600: '#a8231b',
          700: '#8a1a14',
          800: '#6e1510',
          900: '#5e120d',
        },
        /* Retuned for AA at the 11-12px these actually render at, on warm paper. */
        signal: {
          trust: '#0b7a55',
          caution: '#9a6508',
          hype: '#b23a22',
        },
      },
      fontFamily: {
        /*
         * The broadsheet is set in two faces and no more.
         *
         * `serif` is the voice — headlines and body are the same family at
         * different optical sizes, which is what makes a front page read as one
         * page rather than as a headline pasted above some text.
         *
         * `sans` is furniture only: flags, folios, figures, nav. Archivo's width
         * axis is set per-use in globals.css, because Turkish labels need the
         * narrow cut to fit slots English would leave room in.
         *
         * `mono` is deliberately absent — the terminal costume is gone, and every
         * former font-mono is now condensed Archivo caps.
         */
        serif: ['var(--font-display)', 'Georgia', 'serif'],
        sans: ['var(--font-gothic)', 'system-ui', '-apple-system', 'sans-serif'],
      },
      maxWidth: {
        reader: '46rem',
      },
      /*
       * A printed page does not animate in. `fade-up` ran on every card in the
       * feed, which on a phone means the whole screen breathing on each scroll
       * back; `blink` belonged to the terminal cursor that this direction
       * removes. Both are deleted rather than left unused.
       */
    },
  },
  plugins: [],
};

export default config;
