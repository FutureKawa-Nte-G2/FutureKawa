import type { NextConfig } from "next";

// Two lockfiles are visible from here: this project's, and an unrelated one in
// the user's home directory. Left alone, Next picks the outermost as the
// workspace root and Turbopack watches that whole tree — unrelated file changes
// trigger rebuilds, and module resolution starts from a directory that has no
// node_modules, so `@import "tailwindcss"` fails to resolve.
//
// process.cwd() rather than a path derived from this file: Next compiles the
// config before running it, so import.meta.url points at the compiled copy and
// resolves one directory too high. Every script in package.json runs from this
// directory, which makes cwd the project root by definition.
const nextConfig: NextConfig = {
  turbopack: {
    root: process.cwd(),
  },
};

export default nextConfig;
