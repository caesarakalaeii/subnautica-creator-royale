// Reads a stylesheet the way a browser parses it. Not a `.test.js`, so it is not run as a suite.

/** Every rule in a stylesheet as `{ selector, body }`, with comments stripped first so a comment cannot swallow a selector. */
export function rules(css) {
  const stripped = css.replace(/\/\*[\s\S]*?\*\//g, " ");

  return [...stripped.matchAll(/([^{}]+)\{([^{}]*)\}/g)].map((match) => ({
    selector: match[1].trim(),
    body: match[2],
  }));
}
