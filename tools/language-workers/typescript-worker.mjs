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
  result.tsconfigs.push({
    path: rel(file),
    extends: json?.extends || "",
    jsx: json?.compilerOptions?.jsx || "",
    module: json?.compilerOptions?.module || "",
    target: json?.compilerOptions?.target || ""
  });
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

  for (let i = 0; i < lines.length; i++) {
    const line = lines[i].trim();
    let match = line.match(/^import\s+(?:.+?\s+from\s+)?["']([^"']+)["']/);
    if (match) {
      result.imports.push({ path: relativePath, line: i + 1, source: match[1] });
    }

    match = line.match(/^export\s+(?:default\s+)?(?:async\s+)?function\s+([A-Za-z_$][\w$]*)/);
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
  }

  if (/(^|\/)(pages|app|routes)\//i.test(relativePath)) {
    result.routes.push({ path: relativePath, kind: "file-route" });
  }
}

function addSymbol(relativePath, line, kind, name, source) {
  result.symbols.push({ path: relativePath, line, kind, name, source, language: /\.tsx?$/i.test(relativePath) ? "TypeScript" : "JavaScript" });
}

function main() {
  detectPackageManagers();
  for (const file of walk(root)) {
    analyzeSource(file);
  }

  for (const file of findNamed(root, "package.json")) {
    analyzePackageJson(file);
  }

  for (const file of findNamed(root, "tsconfig.json")) {
    analyzeTsconfig(file);
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
