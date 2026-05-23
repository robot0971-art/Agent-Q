import fs from "node:fs";
import path from "node:path";
import { execFileSync } from "node:child_process";

const root = path.resolve(process.argv[2] || process.cwd());
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
  "target",
  "dist",
]);

const result = {
  worker: "native-worker",
  version: 1,
  root,
  cpp: {
    cmakeProjects: [],
    compileCommands: [],
    compileCommandCount: 0,
    vcxprojects: [],
    sourceFiles: [],
    headerFiles: [],
    tooling: [],
  },
  go: {
    modules: [],
    packages: [],
    sourceFiles: [],
    tooling: [],
  },
  rust: {
    manifests: [],
    packages: [],
    targets: [],
    sourceFiles: [],
    tooling: [],
  },
  projectMap: [],
  warnings: [],
};

function rel(file) {
  return path.relative(root, file).replaceAll(path.sep, "/") || ".";
}

function exists(file) {
  try {
    return fs.existsSync(file);
  } catch {
    return false;
  }
}

function readText(file) {
  try {
    return fs.readFileSync(file, "utf8");
  } catch {
    return "";
  }
}

function walk(current, visit) {
  let entries = [];
  try {
    entries = fs.readdirSync(current, { withFileTypes: true });
  } catch {
    return;
  }

  for (const entry of entries) {
    if (entry.isDirectory()) {
      if (!ignored.has(entry.name)) {
        walk(path.join(current, entry.name), visit);
      }
      continue;
    }

    visit(path.join(current, entry.name), entry.name);
  }
}

function findFiles(name) {
  const matches = [];
  walk(root, (file, fileName) => {
    if (fileName === name) {
      matches.push(file);
    }
  });
  return matches;
}

function findByExtension(extensions, max = 40) {
  const matches = [];
  walk(root, (file) => {
    if (matches.length >= max) {
      return;
    }

    if (extensions.includes(path.extname(file).toLowerCase())) {
      matches.push(file);
    }
  });
  return matches;
}

function addProjectMap(role, itemPath) {
  if (!itemPath) {
    return;
  }

  const entry = { role, path: itemPath };
  if (!result.projectMap.some((item) => item.role === entry.role && item.path === entry.path)) {
    result.projectMap.push(entry);
  }
}

function detectCpp() {
  for (const cmake of findFiles("CMakeLists.txt")) {
    const text = readText(cmake);
    const projectMatch = text.match(/\bproject\s*\(\s*([A-Za-z_][\w.-]*)/i);
    result.cpp.cmakeProjects.push({
      path: rel(cmake),
      name: projectMatch?.[1] || "",
    });
    addProjectMap("CMake projects", path.dirname(rel(cmake)) || ".");
  }

  for (const compileCommands of findFiles("compile_commands.json")) {
    let count = 0;
    try {
      const parsed = JSON.parse(readText(compileCommands));
      count = Array.isArray(parsed) ? parsed.length : 0;
    } catch {
      result.warnings.push(`Could not parse ${rel(compileCommands)}`);
    }

    result.cpp.compileCommands.push({ path: rel(compileCommands), count });
    result.cpp.compileCommandCount += count;
    addProjectMap("C++ compile database", path.dirname(rel(compileCommands)) || ".");
  }

  result.cpp.vcxprojects = findByExtension([".vcxproj"], 20).map((file) => ({ path: rel(file) }));
  result.cpp.sourceFiles = findByExtension([".cpp", ".cc", ".cxx", ".c"], 30).map(rel);
  result.cpp.headerFiles = findByExtension([".h", ".hpp", ".hh", ".hxx"], 30).map(rel);

  if (result.cpp.vcxprojects.length > 0) {
    addProjectMap("Visual C++ projects", path.dirname(result.cpp.vcxprojects[0].path) || ".");
  }
  if (result.cpp.sourceFiles.length > 0) {
    addProjectMap("C++ source", path.dirname(result.cpp.sourceFiles[0]) || ".");
  }
  if (result.cpp.headerFiles.length > 0) {
    addProjectMap("C++ headers", path.dirname(result.cpp.headerFiles[0]) || ".");
  }
  if (result.cpp.compileCommands.length > 0) {
    result.cpp.tooling.push("compile_commands.json");
  }
  if (result.cpp.cmakeProjects.length > 0) {
    result.cpp.tooling.push("CMake");
  }
}

function parseGoMod(file) {
  const text = readText(file);
  const moduleMatch = text.match(/^\s*module\s+(.+)$/m);
  const goMatch = text.match(/^\s*go\s+([0-9.]+)$/m);
  return {
    path: rel(file),
    module: moduleMatch?.[1]?.trim() || "",
    goVersion: goMatch?.[1]?.trim() || "",
  };
}

function detectGo() {
  result.go.modules = findFiles("go.mod").map(parseGoMod);
  result.go.sourceFiles = findByExtension([".go"], 40).map(rel);
  if (result.go.modules.length > 0) {
    result.go.tooling.push("go modules");
    addProjectMap("Go modules", path.dirname(result.go.modules[0].path) || ".");
  }
  if (result.go.sourceFiles.length > 0) {
    addProjectMap("Go packages", path.dirname(result.go.sourceFiles[0]) || ".");
  }

  try {
    const output = execFileSync("go", ["list", "-json", "./..."], {
      cwd: root,
      encoding: "utf8",
      timeout: 8000,
      windowsHide: true,
      stdio: ["ignore", "pipe", "pipe"],
    });
    result.go.packages = parseConcatenatedJson(output).slice(0, 60).map((pkg) => ({
      importPath: pkg.ImportPath || "",
      directory: pkg.Dir ? rel(pkg.Dir) : "",
      name: pkg.Name || "",
    }));
    if (result.go.packages.length > 0) {
      result.go.tooling.push("go list");
    }
  } catch (error) {
    if (result.go.modules.length > 0) {
      result.warnings.push(`go list unavailable: ${shortError(error)}`);
    }
  }
}

function parseCargoToml(file) {
  const text = readText(file);
  const packageName = text.match(/^\s*name\s*=\s*["']([^"']+)["']/m)?.[1] || "";
  const workspace = /^\s*\[workspace\]/m.test(text);
  return {
    path: rel(file),
    packageName,
    isWorkspace: workspace,
  };
}

function detectRust() {
  result.rust.manifests = findFiles("Cargo.toml").map(parseCargoToml);
  result.rust.sourceFiles = findByExtension([".rs"], 40).map(rel);
  if (result.rust.manifests.length > 0) {
    result.rust.tooling.push("Cargo");
    addProjectMap("Cargo manifests", path.dirname(result.rust.manifests[0].path) || ".");
  }
  if (result.rust.sourceFiles.length > 0) {
    addProjectMap("Rust crates", path.dirname(result.rust.sourceFiles[0]) || ".");
  }

  try {
    const output = execFileSync("cargo", ["metadata", "--format-version", "1", "--no-deps"], {
      cwd: root,
      encoding: "utf8",
      timeout: 8000,
      windowsHide: true,
      stdio: ["ignore", "pipe", "pipe"],
    });
    const metadata = JSON.parse(output);
    result.rust.packages = (metadata.packages || []).slice(0, 60).map((pkg) => ({
      name: pkg.name || "",
      version: pkg.version || "",
      manifestPath: pkg.manifest_path ? rel(pkg.manifest_path) : "",
    }));
    result.rust.targets = (metadata.packages || []).flatMap((pkg) =>
      (pkg.targets || []).map((target) => ({
        packageName: pkg.name || "",
        name: target.name || "",
        kind: Array.isArray(target.kind) ? target.kind.join(",") : "",
        sourcePath: target.src_path ? rel(target.src_path) : "",
      })),
    ).slice(0, 80);
    if (result.rust.packages.length > 0) {
      result.rust.tooling.push("cargo metadata");
    }
  } catch (error) {
    if (result.rust.manifests.length > 0) {
      result.warnings.push(`cargo metadata unavailable: ${shortError(error)}`);
    }
  }
}

function parseConcatenatedJson(text) {
  const values = [];
  let depth = 0;
  let start = -1;
  let inString = false;
  let escaped = false;

  for (let index = 0; index < text.length; index += 1) {
    const char = text[index];
    if (inString) {
      escaped = char === "\\" && !escaped;
      if (char === "\"" && !escaped) {
        inString = false;
      } else if (char !== "\\") {
        escaped = false;
      }
      continue;
    }

    if (char === "\"") {
      inString = true;
    } else if (char === "{") {
      if (depth === 0) {
        start = index;
      }
      depth += 1;
    } else if (char === "}") {
      depth -= 1;
      if (depth === 0 && start >= 0) {
        values.push(JSON.parse(text.slice(start, index + 1)));
      }
    }
  }

  return values;
}

function shortError(error) {
  const stderr = error?.stderr?.toString?.() || "";
  const message = stderr || error?.message || "command failed";
  return message.split(/\r?\n/).find((line) => line.trim().length > 0)?.trim() || "command failed";
}

detectCpp();
detectGo();
detectRust();

console.log(JSON.stringify(result));
