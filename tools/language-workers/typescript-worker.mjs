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
  routes: [],
  symbols: [],
  projectMap: [],
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

  for (const [name, command] of Object.entries(json.scripts || {})) {
    result.npmScripts.push({ packagePath, name, command: String(command) });
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
  }

  if (/(^|\/)(pages|app|routes)\//i.test(relativePath)) {
    result.routes.push({ path: relativePath, kind: "file-route" });
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

  console.log(JSON.stringify(result));
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

main();
