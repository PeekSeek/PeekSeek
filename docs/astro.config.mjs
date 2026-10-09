import starlight from "@astrojs/starlight";
import { defineConfig } from "astro/config";
import starlightLinksValidator from "starlight-links-validator";
import starlightOpenAPI, { openAPISidebarGroups } from "starlight-openapi";

// The site mounts this build at /docs (see website/src/app.py), so the base has to
// match or every asset URL and sidebar link comes out one level too high.
const base = "/docs";

export default defineConfig({
  site: "https://peekseek.net",
  base,
  trailingSlash: "always",
  integrations: [
    starlight({
      title: "PeekSeek Docs",
      description: "Run PeekSeek yourself, or fork it for another game",
      // Copied from website/public/img rather than referenced: the docs build has to
      // stand on its own in dev, where /static is not being served.
      // An SVG that carries both marks and swaps them with its own media query.
      // Starlight always emits this tag last, and a later icon link wins, so the
      // switching has to happen inside the file rather than between two links.
      favicon: "/favicon.svg",
      // Fallback for browsers that do not take an SVG icon. These come first, so a
      // browser that understands the SVG above uses it instead.
      head: [
        {
          tag: "link",
          attrs: { rel: "icon", href: `${base}/favicon.ico`, type: "image/x-icon" },
        },
        {
          tag: "link",
          attrs: {
            rel: "icon",
            href: `${base}/favicon-light.ico`,
            type: "image/x-icon",
            media: "(prefers-color-scheme: dark)",
          },
        },
      ],
      // replacesTitle drops the "PeekSeek Docs" wordmark beside the mark, which already
      // says it. The title still carries the browser tab, search results and metadata.
      logo: { src: "./src/assets/cmw.png", alt: "PeekSeek", replacesTitle: true },
      customCss: ["./src/styles/peekseek.css"],
      social: [
        {
          icon: "github",
          label: "GitHub",
          href: "https://github.com/PeekSeek/PeekSeek",
        },
        {
          icon: "discord",
          label: "Discord",
          href: "https://discord.gg/NKgTe6RnvH",
        },
      ],
      editLink: {
        baseUrl: "https://github.com/PeekSeek/PeekSeek/edit/main/docs/",
      },
      plugins: [
        // MkDocs built these pages with --strict, where a broken internal link fails
        // the build. That is the one docs error worth blocking a merge for, and
        // nothing in Astro checks it otherwise.
        starlightLinksValidator({
          errorOnRelativeLinks: false,
          // Getting Started tells you to open localhost:8000, which is the point.
          errorOnLocalLinks: false,
          // The OpenAPI plugin's own overview route is generated after validation
          // runs, so the validator cannot see it. It is built: dist/reference/
          // server-api/index.html.
          exclude: ["/docs/reference/server-api/"],
        }),
        // Generated from the running app by website/scripts/openapi.py, so the route
        // reference is the code's own schema and not a second copy to keep in step.
        starlightOpenAPI([
          {
            base: "reference/server-api",
            label: "Server API reference",
            schema: "./openapi.json",
            collapsed: true,
          },
        ]),
      ],
      sidebar: [
        { label: "Home", link: "/" },
        { label: "Getting started", link: "/getting-started/" },
        {
          label: "How it works",
          collapsed: true,
          items: [
            { label: "Architecture", link: "/architecture/" },
            { label: "The match lifecycle", link: "/match-lifecycle/" },
          ],
        },
        {
          label: "Running your own",
          collapsed: true,
          items: [
            { label: "Self-hosting", link: "/self-hosting/" },
            { label: "Configuration", link: "/configuration/" },
          ],
        },
        {
          label: "Forking",
          collapsed: true,
          items: [{ label: "Another game", link: "/forking/" }],
        },
        {
          label: "The game-server API",
          collapsed: true,
          items: [{ label: "The contract", link: "/reference/api/" }],
        },
        ...openAPISidebarGroups,
      ],
    }),
  ],
});
