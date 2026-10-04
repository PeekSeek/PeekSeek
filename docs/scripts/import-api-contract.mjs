/* website/API.md is the hand-written half of the game-server contract: the conventions,
   the idea, and the prose no generated page can produce. It stays where it is, next to
   the code it describes and readable on GitHub, and is imported here at build time so
   the docs site carries one copy rather than a second one to keep in step.

   The generated page is gitignored. Edit website/API.md, never src/content/docs/
   reference/api.md. */

import { existsSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";

const REPO = "https://github.com/PeekSeek/PeekSeek/blob/main/website";
const OUT = "src/content/docs/reference/api.md";

// The schema is the build's other generated input and needs the website's Python deps,
// so it cannot be produced from here. Fail with the command rather than letting Astro
// report a missing file it knows nothing about.
if (!existsSync("openapi.json")) {
  console.error("openapi.json is missing. Generate it first: npm run schema");
  process.exit(1);
}

let body = readFileSync("../website/API.md", "utf8");

// API.md has CRLF endings, and JavaScript's `.` does not match a carriage return, so
// without this the heading below silently fails to match and the title appears twice.
body = body.replace(/\r\n/g, "\n");
body = body.replace(/^#\s+.*\n+/, ""); // Starlight renders the title from frontmatter

// API.md's links are relative to website/, which does not exist under /docs.
body = body.replace(/\]\((src\/[^)]+)\)/g, `](${REPO}/$1)`);

mkdirSync("src/content/docs/reference", { recursive: true });
writeFileSync(
  OUT,
  [
    "---",
    "title: The game-server API",
    "description: The conventions, the idea, and the contract every route follows",
    "sidebar:",
    "  order: 7",
    "---",
    "",
    body,
  ].join("\n"),
);
console.log(`wrote ${OUT}`);
