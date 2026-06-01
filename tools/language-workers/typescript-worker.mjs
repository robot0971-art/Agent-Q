import fs from "node:fs";
import path from "node:path";

const root = path.resolve(process.argv[2] || process.cwd());
const maxFiles = 600;
const ignored = new Set([
  ".git",
  ".vs",
  "bin",
  "obj",
  "node_modules",
  "artifacts",
  ".codex-build",
  ".venv",
  "venv",
  "env",
  "__pycache__",
  ".agentq",
  "dist",
  "build",
  ".next",
  "coverage"
]);

const result = {
  worker: "typescript-worker",
  version: 1,
  root,
  packageManagers: [],
  packages: [],
  tsconfigs: [],
  npmScripts: [],
  imports: [],
  exports: [],
  reactComponents: [],
  reactHooks: [],
  apiEndpoints: [],
  testTargets: [],
  playwright: {
    hasDependency: false,
    configs: [],
    scripts: [],
    reportPaths: []
  },
  routes: [],
  symbols: [],
  projectMap: [],
  capabilities: [],
  scaffoldRecommendations: [],
  warnings: []
};

const sourceFiles = new Map();
const tsconfigAliases = [];

function rel(file) {
  return path.relative(root, file).replaceAll(path.sep, "/");
}

function safeRead(file) {
  try {
    return fs.readFileSync(file, "utf8");
  } catch {
    return "";
  }
}

function safeJson(file) {
  try {
    return JSON.parse(safeRead(file));
  } catch {
    result.warnings.push(`Could not parse ${rel(file)}`);
    return null;
  }
}

function walk(dir, files = []) {
  if (files.length >= maxFiles) {
    return files;
  }

  let entries = [];
  try {
    entries = fs.readdirSync(dir, { withFileTypes: true });
  } catch {
    return files;
  }

  for (const entry of entries) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      if (!ignored.has(entry.name)) {
        walk(full, files);
      }
      continue;
    }

    if (/\.(jsx?|tsx?)$/i.test(entry.name)) {
      files.push(full);
      if (files.length >= maxFiles) {
        break;
      }
    }
  }

  return files;
}

function addProjectMap(role, value) {
  if (!value || result.projectMap.some((item) => item.role === role && item.path === value)) {
    return;
  }

  result.projectMap.push({ role, path: value });
}

function analyzePackageJson(file) {
  const json = safeJson(file);
  if (!json) {
    return;
  }

  const packagePath = rel(file);
  result.packages.push({
    path: packagePath,
    name: json.name || "",
    type: json.type || "",
    dependencies: Object.keys(json.dependencies || {}),
    devDependencies: Object.keys(json.devDependencies || {})
  });

  const dependencyNames = new Set([
    ...Object.keys(json.dependencies || {}),
    ...Object.keys(json.devDependencies || {})
  ].map((name) => name.toLowerCase()));
  if (dependencyNames.has("@playwright/test") || dependencyNames.has("playwright")) {
    result.playwright.hasDependency = true;
  }

  for (const [name, command] of Object.entries(json.scripts || {})) {
    const script = { packagePath, name, command: String(command) };
    result.npmScripts.push(script);
    if (isPlaywrightScript(name, script.command)) {
      result.playwright.scripts.push(script);
    }
  }

  const dir = path.dirname(packagePath);
  if (dir && dir !== ".") {
    addProjectMap("JavaScript package", dir);
  }
}

function analyzeTsconfig(file) {
  const json = safeJson(file);
  const configDir = path.dirname(file);
  const compilerOptions = json?.compilerOptions || {};
  result.tsconfigs.push({
    path: rel(file),
    extends: json?.extends || "",
    jsx: compilerOptions.jsx || "",
    module: compilerOptions.module || "",
    target: compilerOptions.target || "",
    baseUrl: compilerOptions.baseUrl || "",
    paths: compilerOptions.paths || {}
  });

  const baseUrl = compilerOptions.baseUrl
    ? path.resolve(configDir, compilerOptions.baseUrl)
    : configDir;
  for (const [alias, targets] of Object.entries(compilerOptions.paths || {})) {
    for (const target of Array.isArray(targets) ? targets : []) {
      tsconfigAliases.push({
        alias,
        target,
        baseUrl
      });
    }
  }
}

function detectPackageManagers() {
  for (const file of ["pnpm-lock.yaml", "yarn.lock", "package-lock.json", "bun.lockb"]) {
    if (fs.existsSync(path.join(root, file))) {
      result.packageManagers.push(file);
    }
  }
}

function analyzeSource(file) {
  const text = safeRead(file);
  if (!text) {
    return;
  }

  const relativePath = rel(file);
  const lines = text.split(/\r?\n/);
  const importRegexes = [
    /\bimport\s+(?:type\s+)?(?:.+?\s+from\s+)?["']([^"']+)["']/g,
    /\bimport\s*\(\s*["']([^"']+)["']\s*\)/g,
    /\bexport\s+(?:type\s+)?(?:.+?\s+from\s+)?["']([^"']+)["']/g,
    /\brequire\s*\(\s*["']([^"']+)["']\s*\)/g
  ];

  for (let i = 0; i < lines.length; i++) {
    const line = lines[i].trim();
    for (const regex of importRegexes) {
      regex.lastIndex = 0;
      let match;
      while ((match = regex.exec(line)) !== null) {
        result.imports.push({
          path: relativePath,
          line: i + 1,
          source: match[1],
          resolvedPath: resolveImport(file, match[1]) || ""
        });
      }
    }

    let match = line.match(/^export\s+(?:default\s+)?(?:async\s+)?function\s+([A-Za-z_$][\w$]*)/);
    if (match) {
      addSymbol(relativePath, i + 1, "function", match[1], "export");
      result.exports.push({ path: relativePath, line: i + 1, name: match[1], kind: "function" });
    }

    match = line.match(/^export\s+(?:default\s+)?class\s+([A-Za-z_$][\w$]*)/);
    if (match) {
      addSymbol(relativePath, i + 1, "class", match[1], "export");
      result.exports.push({ path: relativePath, line: i + 1, name: match[1], kind: "class" });
    }

    match = line.match(/^export\s+(?:const|let|var)\s+([A-Za-z_$][\w$]*)\s*=/);
    if (match) {
      addSymbol(relativePath, i + 1, "const", match[1], "export");
      result.exports.push({ path: relativePath, line: i + 1, name: match[1], kind: "const" });
    }

    match = line.match(/^(?:export\s+)?(?:const|function)\s+([A-Z][A-Za-z0-9_$]*)/);
    if (match && /\.(jsx|tsx)$/i.test(relativePath)) {
      result.reactComponents.push({ path: relativePath, line: i + 1, name: match[1] });
    }

    match = line.match(/^(?:export\s+)?(?:const|function)\s+(use[A-Z][A-Za-z0-9_$]*)/);
    if (match && /\.(jsx|tsx?)$/i.test(relativePath)) {
      addUniqueByPathLineName(result.reactHooks, { path: relativePath, line: i + 1, name: match[1] });
    }

    match = line.match(/^(?:export\s+)?(?:default\s+)?(?:async\s+)?function\s+([A-Za-z_$][\w$]*)/);
    if (match) {
      addSymbolIfMissing(relativePath, i + 1, "function", match[1], line.startsWith("export") ? "export" : "declaration");
    }

    match = line.match(/^(?:export\s+)?(?:default\s+)?class\s+([A-Za-z_$][\w$]*)/);
    if (match) {
      addSymbolIfMissing(relativePath, i + 1, "class", match[1], line.startsWith("export") ? "export" : "declaration");
    }

    match = line.match(/^(?:export\s+)?(?:const|let|var)\s+([A-Za-z_$][\w$]*)\s*=/);
    if (match) {
      addSymbolIfMissing(relativePath, i + 1, "const", match[1], line.startsWith("export") ? "export" : "declaration");
    }

    match = line.match(/^(?:export\s+)?(?:async\s+)?function\s+(GET|POST|PUT|PATCH|DELETE|HEAD|OPTIONS)\s*\(/);
    if (match && /(^|\/)(app|pages|api)\//i.test(relativePath)) {
      addUniqueByPathLineName(result.apiEndpoints, {
        path: relativePath,
        line: i + 1,
        method: match[1],
        route: routeFromFile(relativePath),
        kind: "handler"
      });
    }

    match = line.match(/\b(test|it|describe)\s*\(\s*["'`]([^"'`]+)["'`]/);
    if (match && /\.(test|spec)\.(jsx?|tsx?)$/i.test(relativePath)) {
      addUniqueByPathLineName(result.testTargets, {
        path: relativePath,
        line: i + 1,
        kind: match[1],
        name: match[2]
      });
    }
  }

  if (/(^|\/)(pages|app|routes)\//i.test(relativePath)) {
    result.routes.push({ path: relativePath, kind: "file-route" });
  }

  if (/(^|\/)(__tests__|tests?)\//i.test(relativePath) || /\.(test|spec)\.(jsx?|tsx?)$/i.test(relativePath)) {
    addUniqueByPathLineName(result.testTargets, {
      path: relativePath,
      line: 0,
      kind: "test-file",
      name: path.basename(relativePath)
    });
  }
}

function detectPlaywrightArtifacts() {
  const configNames = [
    "playwright.config.ts",
    "playwright.config.js",
    "playwright.config.mjs",
    "playwright.config.cjs",
    "playwright.config.mts",
    "playwright.config.cts"
  ];

  for (const name of configNames) {
    for (const file of findNamed(root, name)) {
      const relativePath = rel(file);
      if (!result.playwright.configs.includes(relativePath)) {
        result.playwright.configs.push(relativePath);
        addProjectMap("Playwright config", relativePath);
      }
    }
  }

  for (const name of ["playwright-report", "test-results"]) {
    for (const directory of findDirectoryNamed(root, name)) {
      const relativePath = rel(directory);
      if (!result.playwright.reportPaths.includes(relativePath)) {
        result.playwright.reportPaths.push(relativePath);
      }
    }
  }
}

function isPlaywrightScript(name, command) {
  const normalizedName = String(name).toLowerCase();
  const normalizedCommand = String(command).toLowerCase();
  return normalizedName.includes("playwright") ||
    normalizedName.includes("e2e") ||
    normalizedCommand.includes("playwright test");
}

function routeFromFile(relativePath) {
  let route = relativePath
    .replace(/\.(jsx?|tsx?)$/i, "")
    .replace(/^.*\/app\//i, "/")
    .replace(/^.*\/pages\//i, "/")
    .replace(/^.*\/api\//i, "/api/")
    .replace(/\/route$/i, "")
    .replace(/\/index$/i, "")
    .replace(/\[(\.{3})?([^\]]+)\]/g, ":$2");
  route = route.replaceAll("\\", "/").replace(/\/+/g, "/");
  return route.startsWith("/") ? route : `/${route}`;
}

function addUniqueByPathLineName(list, item) {
  const name = item.name || item.method || item.kind || "";
  if (!list.some((existing) => existing.path === item.path && existing.line === item.line && (existing.name || existing.method || existing.kind || "") === name)) {
    list.push(item);
  }
}

function addSymbol(relativePath, line, kind, name, source) {
  result.symbols.push({ path: relativePath, line, kind, name, source, language: /\.tsx?$/i.test(relativePath) ? "TypeScript" : "JavaScript" });
}

function addSymbolIfMissing(relativePath, line, kind, name, source) {
  if (!result.symbols.some((item) => item.path === relativePath && item.line === line && item.kind === kind && item.name === name)) {
    addSymbol(relativePath, line, kind, name, source);
  }
}

function buildSourceFileIndex(files) {
  sourceFiles.clear();
  for (const file of files) {
    sourceFiles.set(rel(file), file);
  }
}

function resolveImport(fromFile, source) {
  if (!source || /^[a-z@][^./]/i.test(source)) {
    return "";
  }

  if (source.startsWith(".")) {
    return resolveCandidate(path.resolve(path.dirname(fromFile), source));
  }

  for (const alias of tsconfigAliases) {
    const resolved = resolveAlias(alias, source);
    if (resolved) {
      return resolved;
    }
  }

  return "";
}

function resolveAlias(alias, source) {
  const starIndex = alias.alias.indexOf("*");
  if (starIndex < 0) {
    if (source !== alias.alias) {
      return "";
    }

    return resolveCandidate(path.resolve(alias.baseUrl, alias.target));
  }

  const prefix = alias.alias.slice(0, starIndex);
  const suffix = alias.alias.slice(starIndex + 1);
  if (!source.startsWith(prefix) || (suffix && !source.endsWith(suffix))) {
    return "";
  }

  const middle = source.slice(prefix.length, suffix ? -suffix.length : undefined);
  return resolveCandidate(path.resolve(alias.baseUrl, alias.target.replace("*", middle)));
}

function resolveCandidate(basePath) {
  const candidates = [
    basePath,
    ...[".ts", ".tsx", ".js", ".jsx", ".mjs", ".cjs"].map((ext) => `${basePath}${ext}`),
    ...[".ts", ".tsx", ".js", ".jsx"].map((ext) => path.join(basePath, `index${ext}`))
  ];

  for (const candidate of candidates) {
    if (!candidate.startsWith(root)) {
      continue;
    }

    const relative = rel(candidate);
    if (sourceFiles.has(relative) || fs.existsSync(candidate)) {
      return relative;
    }
  }

  return "";
}

function main() {
  detectPackageManagers();
  detectPlaywrightArtifacts();
  const files = walk(root);
  for (const file of findNamed(root, "tsconfig.json")) {
    analyzeTsconfig(file);
  }

  buildSourceFileIndex(files);
  for (const file of files) {
    analyzeSource(file);
  }

  for (const file of findNamed(root, "package.json")) {
    analyzePackageJson(file);
  }

  addGenerationGuidance();
  console.log(JSON.stringify(result));
}

function addGenerationGuidance() {
  const dependencies = new Set(
    result.packages.flatMap((pkg) => [...pkg.dependencies, ...pkg.devDependencies]).map((name) => name.toLowerCase())
  );
  const hasProjectShape = result.packages.length > 0 ||
    result.tsconfigs.length > 0 ||
    result.imports.length > 0 ||
    result.exports.length > 0 ||
    result.reactComponents.length > 0 ||
    result.routes.length > 0 ||
    result.testTargets.length > 0;
  const hasRunnableAppEntry = fileExists("index.html") &&
    (fileExists("src/main.tsx") ||
      fileExists("src/main.jsx") ||
      fileExists("src/main.ts") ||
      fileExists("src/main.js") ||
      fileExists("src/App.tsx") ||
      fileExists("src/App.jsx"));

  addCapability("analyze-js-ts", "Analyze JavaScript/TypeScript imports, exports, components, hooks, routes, tests, and package scripts.");
  addCapability("extend-react-ui", "Extend React/Vite/Next.js UI surfaces using detected components, hooks, and route files.");
  addCapability("create-vite-react-project", "Create a new Vite React TypeScript project with package scripts, entrypoint, app shell, styles, and build verification.");

  if (!hasProjectShape ||
    !hasRunnableAppEntry ||
    (dependencies.size === 0 && result.imports.length === 0 && result.exports.length === 0)) {
    addScaffoldRecommendation(
      "Vite React TypeScript project",
      "Create a runnable React/Vite starter project with package.json, TypeScript config, HTML entrypoint, app shell, and styles.",
      ["package.json", "index.html", "vite.config.ts", "tsconfig.json", "src/main.tsx", "src/App.tsx", "src/styles.css"],
      ["npm install", "npm run build"]
    );
  }

  if (dependencies.has("next")) {
    addCapability("create-next-feature", "Create a Next.js feature with App Router pages, route handlers, client/server components, and tests.");
    addScaffoldRecommendation(
      "Next.js full-stack feature",
      "Create app route, API handler, shared lib module, component, hook, and colocated tests.",
      ["app/<feature>/page.tsx", "app/api/<feature>/route.ts", "components/<FeaturePanel>.tsx", "lib/<feature>.ts", "__tests__/<feature>.test.ts"],
      ["npm run lint", "npm test", "npm run build"]
    );
  } else if (dependencies.has("react")) {
    addCapability("create-react-feature", "Create a React feature with component, hook, state boundary, API client, and tests.");
    addScaffoldRecommendation(
      "React application feature",
      "Create component, hook, API module, route/view integration, and Vitest/Jest coverage.",
      ["<feature_dir>/<Feature>View.tsx", "<feature_dir>/use<Feature>.ts", "<feature_dir>/api.ts", "<feature_dir>/<Feature><ts_test_suffix>.tsx"],
      ["npm test", "npm run build"]
    );
  }

  if (dependencies.has("express") || dependencies.has("fastify") || dependencies.has("nestjs") || dependencies.has("@nestjs/core")) {
    addCapability("create-node-api", "Create Node API endpoints with routing, validation, service modules, and tests.");
    addScaffoldRecommendation(
      "Node API module",
      "Create route/controller, service, validation schema, and request-level tests.",
      ["src/routes/<feature>.ts", "src/services/<feature>.ts", "src/schemas/<feature>.ts", "src/routes/<feature>.test.ts"],
      ["npm test", "npm run build"]
    );
  }

  if (result.playwright.hasDependency || result.playwright.configs.length > 0 || result.playwright.scripts.length > 0) {
    addCapability("verify-playwright", "Run Playwright browser checks and use screenshots/reports as UI verification evidence.");
  }
}

function fileExists(relativePath) {
  return fs.existsSync(path.join(root, relativePath));
}

function addCapability(name, description) {
  if (!result.capabilities.some((item) => item.name === name)) {
    result.capabilities.push({ name, description });
  }
}

function addScaffoldRecommendation(name, description, files, verificationCommands) {
  if (!result.scaffoldRecommendations.some((item) => item.name === name)) {
    result.scaffoldRecommendations.push({ name, description, files, verificationCommands });
  }
}

function findNamed(dir, name, results = []) {
  let entries = [];
  try {
    entries = fs.readdirSync(dir, { withFileTypes: true });
  } catch {
    return results;
  }

  for (const entry of entries) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      if (!ignored.has(entry.name)) {
        findNamed(full, name, results);
      }
    } else if (entry.name === name) {
      results.push(full);
    }
  }

  return results;
}

function findDirectoryNamed(dir, name, results = []) {
  let entries = [];
  try {
    entries = fs.readdirSync(dir, { withFileTypes: true });
  } catch {
    return results;
  }

  for (const entry of entries) {
    const full = path.join(dir, entry.name);
    if (!entry.isDirectory()) {
      continue;
    }

    if (ignored.has(entry.name)) {
      continue;
    }

    if (entry.name === name) {
      results.push(full);
      continue;
    }

    findDirectoryNamed(full, name, results);
  }

  return results;
}

main();
