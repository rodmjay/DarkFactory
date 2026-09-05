import { defineConfig, globalIgnores } from "eslint/config";
import tseslint from "typescript-eslint";
import reactHooks from "eslint-plugin-react-hooks";

export default defineConfig([
  globalIgnores(["node_modules/**"]),
  ...tseslint.configs.recommended,
  reactHooks.configs.flat["recommended-latest"],
  {
    files: ["**/*.{ts,tsx}"],
    rules: {
      // Radix wrappers spread `...props` onto primitives whose types are
      // structural; the empty-object-type complaint is noise there.
      "@typescript-eslint/no-empty-object-type": "off",
    },
  },
]);
