import type { Config } from 'tailwindcss';

/**
 * A deliberately calm palette. The product's premise is that it replaces
 * attention-farming feeds, so the design language avoids saturated alert
 * colours except where they carry real meaning (trust, hype, urgency).
 */
const config: Config = {
  content: ['./src/**/*.{ts,tsx}'],
  darkMode: 'class',
  theme: {
    extend: {
      colors: {
        ink: {
          50: '#f6f7f9',
          100: '#eceef2',
          200: '#d4d9e2',
          300: '#aeb7c8',
          400: '#8290a9',
          500: '#61708d',
          600: '#4c5a74',
          700: '#3f4a5e',
          800: '#363f4f',
          900: '#0f1521',
          950: '#080b12',
        },
        focus: {
          50: '#eef4ff',
          100: '#d9e6ff',
          200: '#bcd4ff',
          300: '#8eb8ff',
          400: '#5991ff',
          500: '#336aff',
          600: '#1c48f5',
          700: '#1636e1',
          800: '#182eb6',
          900: '#1a2d8f',
        },
        signal: {
          trust: '#0f9d76',
          caution: '#c2820b',
          hype: '#d1553b',
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
      animation: {
        'fade-up': 'fade-up 0.35s ease-out both',
        blink: 'blink 1.15s step-end infinite',
      },
      keyframes: {
        'fade-up': {
          '0%': { opacity: '0', transform: 'translateY(6px)' },
          '100%': { opacity: '1', transform: 'translateY(0)' },
        },
        blink: {
          '50%': { opacity: '0' },
        },
      },
    },
  },
  plugins: [],
};

export default config;
