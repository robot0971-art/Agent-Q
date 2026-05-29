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
  java: {
    buildFiles: [],
    sourceFiles: [],
    testFiles: [],
    symbols: [],
    frameworks: [],
    tooling: [],
  },
  sql: {
    files: [],
    migrations: [],
    tables: [],
    tooling: [],
  },
  php: {
    composerFiles: [],
    sourceFiles: [],
    testFiles: [],
    symbols: [],
    frameworks: [],
    tooling: [],
  },
  kotlin: {
    buildFiles: [],
    sourceFiles: [],
    testFiles: [],
    symbols: [],
    frameworks: [],
    tooling: [],
  },
  swift: {
    packageFiles: [],
    projectFiles: [],
    sourceFiles: [],
    testFiles: [],
    symbols: [],
    frameworks: [],
    tooling: [],
  },
  scripts: {
    shellFiles: [],
    powerShellFiles: [],
    commands: [],
    tooling: [],
  },
  r: {
    projectFiles: [],
    sourceFiles: [],
    reportFiles: [],
    symbols: [],
    tooling: [],
  },
  projectMap: [],
  capabilities: [],
  scaffoldRecommendations: [],
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

function detectJava() {
  result.java.buildFiles = [
    ...findFiles("pom.xml"),
    ...findFiles("build.gradle"),
    ...findFiles("build.gradle.kts"),
    ...findFiles("settings.gradle"),
    ...findFiles("settings.gradle.kts"),
  ].map((file) => ({ path: rel(file) }));
  result.java.sourceFiles = findByExtension([".java"], 60).map(rel);
  result.java.testFiles = result.java.sourceFiles.filter((file) => /(^|\/)src\/test\//i.test(file) || /Test\.java$/i.test(file));

  const buildText = result.java.buildFiles.map((item) => readText(path.join(root, item.path))).join("\n").toLowerCase();
  if (buildText.includes("spring-boot") || buildText.includes("org.springframework")) {
    result.java.frameworks.push("Spring Boot");
  }
  if (buildText.includes("junit")) {
    result.java.frameworks.push("JUnit");
  }
  if (result.java.buildFiles.some((item) => item.path.endsWith("pom.xml"))) {
    result.java.tooling.push("Maven");
  }
  if (result.java.buildFiles.some((item) => item.path.includes("gradle"))) {
    result.java.tooling.push("Gradle");
  }

  for (const file of result.java.sourceFiles.slice(0, 30)) {
    const text = readText(path.join(root, file));
    for (const match of text.matchAll(/\b(public\s+)?(class|interface|enum|record)\s+([A-Za-z_]\w*)/g)) {
      result.java.symbols.push({ path: file, kind: match[2], name: match[3] });
    }
  }

  if (result.java.buildFiles.length > 0) addProjectMap("Java build files", path.dirname(result.java.buildFiles[0].path) || ".");
  if (result.java.sourceFiles.length > 0) addProjectMap("Java source", path.dirname(result.java.sourceFiles[0]) || ".");
  if (result.java.testFiles.length > 0) addProjectMap("Java tests", path.dirname(result.java.testFiles[0]) || ".");
}

function detectSql() {
  result.sql.files = findByExtension([".sql"], 80).map(rel);
  result.sql.migrations = result.sql.files.filter((file) => /migration|migrations|alembic|flyway|liquibase|db/i.test(file));
  for (const file of result.sql.files.slice(0, 40)) {
    const text = readText(path.join(root, file));
    for (const match of text.matchAll(/\bcreate\s+table\s+(?:if\s+not\s+exists\s+)?["`[]?([A-Za-z_][\w.]*)/gi)) {
      result.sql.tables.push({ path: file, name: match[1] });
    }
  }
  if (result.sql.migrations.some((file) => /flyway/i.test(file))) result.sql.tooling.push("Flyway");
  if (result.sql.migrations.some((file) => /liquibase/i.test(file))) result.sql.tooling.push("Liquibase");
  if (result.sql.files.length > 0) addProjectMap("SQL files", path.dirname(result.sql.files[0]) || ".");
  if (result.sql.migrations.length > 0) addProjectMap("SQL migrations", path.dirname(result.sql.migrations[0]) || ".");
}

function detectPhp() {
  result.php.composerFiles = findFiles("composer.json").map((file) => ({ path: rel(file) }));
  result.php.sourceFiles = findByExtension([".php"], 80).map(rel);
  result.php.testFiles = result.php.sourceFiles.filter((file) => /(^|\/)tests?\//i.test(file) || /Test\.php$/i.test(file));
  const composerText = result.php.composerFiles.map((item) => readText(path.join(root, item.path))).join("\n").toLowerCase();
  if (composerText.includes("laravel/framework")) result.php.frameworks.push("Laravel");
  if (composerText.includes("symfony/")) result.php.frameworks.push("Symfony");
  if (composerText.includes("phpunit/phpunit")) result.php.frameworks.push("PHPUnit");
  if (result.php.composerFiles.length > 0) result.php.tooling.push("Composer");
  for (const file of result.php.sourceFiles.slice(0, 30)) {
    const text = readText(path.join(root, file));
    for (const match of text.matchAll(/\b(class|interface|trait|enum|function)\s+([A-Za-z_]\w*)/g)) {
      result.php.symbols.push({ path: file, kind: match[1], name: match[2] });
    }
  }
  if (result.php.composerFiles.length > 0) addProjectMap("PHP packages", path.dirname(result.php.composerFiles[0].path) || ".");
  if (result.php.sourceFiles.length > 0) addProjectMap("PHP source", path.dirname(result.php.sourceFiles[0]) || ".");
}

function detectKotlin() {
  result.kotlin.buildFiles = findFiles("build.gradle.kts").concat(findFiles("settings.gradle.kts")).map((file) => ({ path: rel(file) }));
  result.kotlin.sourceFiles = findByExtension([".kt", ".kts"], 80).map(rel);
  result.kotlin.testFiles = result.kotlin.sourceFiles.filter((file) => /(^|\/)src\/test\//i.test(file) || /Test\.kt$/i.test(file));
  const text = result.kotlin.buildFiles.map((item) => readText(path.join(root, item.path))).join("\n").toLowerCase();
  if (text.includes("com.android.application") || text.includes("com.android.library")) result.kotlin.frameworks.push("Android");
  if (text.includes("ktor")) result.kotlin.frameworks.push("Ktor");
  if (text.includes("spring")) result.kotlin.frameworks.push("Spring");
  if (result.kotlin.buildFiles.length > 0) result.kotlin.tooling.push("Gradle Kotlin DSL");
  for (const file of result.kotlin.sourceFiles.slice(0, 30)) {
    const source = readText(path.join(root, file));
    for (const match of source.matchAll(/\b(class|interface|object|data\s+class|fun)\s+([A-Za-z_]\w*)/g)) {
      result.kotlin.symbols.push({ path: file, kind: match[1].replace(/\s+/g, " "), name: match[2] });
    }
  }
  if (result.kotlin.sourceFiles.length > 0) addProjectMap("Kotlin source", path.dirname(result.kotlin.sourceFiles[0]) || ".");
}

function detectSwift() {
  result.swift.packageFiles = findFiles("Package.swift").map((file) => ({ path: rel(file) }));
  result.swift.projectFiles = findByExtension([".xcodeproj", ".xcworkspace"], 20).map((file) => ({ path: rel(file) }));
  result.swift.sourceFiles = findByExtension([".swift"], 80).map(rel);
  result.swift.testFiles = result.swift.sourceFiles.filter((file) => /tests?\//i.test(file) || /Tests\.swift$/i.test(file));
  const text = result.swift.sourceFiles.slice(0, 20).map((file) => readText(path.join(root, file))).join("\n");
  if (/\bimport\s+SwiftUI\b/.test(text)) result.swift.frameworks.push("SwiftUI");
  if (/\bimport\s+UIKit\b/.test(text)) result.swift.frameworks.push("UIKit");
  if (/\bimport\s+XCTest\b/.test(text)) result.swift.frameworks.push("XCTest");
  if (result.swift.packageFiles.length > 0) result.swift.tooling.push("Swift Package Manager");
  if (result.swift.projectFiles.length > 0) result.swift.tooling.push("Xcode");
  for (const file of result.swift.sourceFiles.slice(0, 30)) {
    const source = readText(path.join(root, file));
    for (const match of source.matchAll(/\b(class|struct|enum|protocol|func|actor)\s+([A-Za-z_]\w*)/g)) {
      result.swift.symbols.push({ path: file, kind: match[1], name: match[2] });
    }
  }
  if (result.swift.sourceFiles.length > 0) addProjectMap("Swift source", path.dirname(result.swift.sourceFiles[0]) || ".");
}

function detectScripts() {
  result.scripts.shellFiles = findByExtension([".sh", ".bash", ".zsh"], 50).map(rel);
  result.scripts.powerShellFiles = findByExtension([".ps1", ".psm1"], 50).map(rel);
  for (const file of [...result.scripts.shellFiles, ...result.scripts.powerShellFiles].slice(0, 40)) {
    const text = readText(path.join(root, file));
    const commands = [...text.matchAll(/^\s*(?:function\s+)?([A-Za-z_][\w-]*)\s*(?:\(\))?/gm)]
      .map((match) => match[1])
      .filter((name) => !["if", "for", "while", "switch"].includes(name));
    for (const command of commands.slice(0, 5)) {
      result.scripts.commands.push({ path: file, name: command });
    }
  }
  if (result.scripts.shellFiles.length > 0) result.scripts.tooling.push("Shell");
  if (result.scripts.powerShellFiles.length > 0) result.scripts.tooling.push("PowerShell");
  if (result.scripts.shellFiles.length > 0) addProjectMap("Shell scripts", path.dirname(result.scripts.shellFiles[0]) || ".");
  if (result.scripts.powerShellFiles.length > 0) addProjectMap("PowerShell scripts", path.dirname(result.scripts.powerShellFiles[0]) || ".");
}

function detectR() {
  result.r.projectFiles = [...findFiles("renv.lock"), ...findFiles("DESCRIPTION")].map((file) => ({ path: rel(file) }));
  result.r.sourceFiles = findByExtension([".r"], 60).map(rel);
  result.r.reportFiles = findByExtension([".rmd", ".qmd"], 40).map(rel);
  if (result.r.projectFiles.some((item) => item.path.endsWith("renv.lock"))) result.r.tooling.push("renv");
  if (result.r.reportFiles.length > 0) result.r.tooling.push("RMarkdown/Quarto");
  for (const file of result.r.sourceFiles.slice(0, 30)) {
    const text = readText(path.join(root, file));
    for (const match of text.matchAll(/^\s*([A-Za-z.][\w.]*)\s*<-\s*function\s*\(/gm)) {
      result.r.symbols.push({ path: file, kind: "function", name: match[1] });
    }
  }
  if (result.r.sourceFiles.length > 0) addProjectMap("R source", path.dirname(result.r.sourceFiles[0]) || ".");
  if (result.r.reportFiles.length > 0) addProjectMap("R reports", path.dirname(result.r.reportFiles[0]) || ".");
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

function addGenerationGuidance() {
  if (result.cpp.cmakeProjects.length > 0 || result.cpp.sourceFiles.length > 0 || result.cpp.headerFiles.length > 0) {
    addCapability("create-cpp-cmake-target", "Create C++ CMake targets with source/header layout, tests, and compile database-aware verification.");
    addScaffoldRecommendation(
      "C++ CMake module",
      "Create library or executable target with headers, implementation, CMake wiring, and tests.",
      ["include/<module>/<module>.hpp", "src/<module>.cpp", "tests/<module>_test.cpp", "CMakeLists.txt"],
      ["cmake -S . -B build", "cmake --build build", "ctest --test-dir build"]
    );
  }

  if (result.go.modules.length > 0 || result.go.sourceFiles.length > 0) {
    addCapability("create-go-service", "Create Go packages with cmd entrypoint, internal service boundaries, HTTP handlers, and tests.");
    addScaffoldRecommendation(
      "Go service package",
      "Create cmd entrypoint, internal package, handler/service split, and go test coverage.",
      ["cmd/<app>/main.go", "internal/<feature>/handler.go", "internal/<feature>/service.go", "internal/<feature>/service_test.go"],
      ["go test ./..."]
    );
  }

  if (result.rust.manifests.length > 0 || result.rust.sourceFiles.length > 0) {
    addCapability("create-rust-crate-feature", "Create Rust crate features with modules, traits, error handling, and tests.");
    addScaffoldRecommendation(
      "Rust crate feature",
      "Create module, public API exports, domain types, error handling, and unit/integration tests.",
      ["src/<feature>/mod.rs", "src/<feature>/error.rs", "src/<feature>/tests.rs", "tests/<feature>_integration.rs"],
      ["cargo fmt --check", "cargo test"]
    );
  }

  if (result.java.buildFiles.length > 0 || result.java.sourceFiles.length > 0) {
    addCapability("create-java-service", "Create Java services with build-tool integration, domain classes, controllers, and tests.");
    addScaffoldRecommendation("Java service feature", "Create controller/service/model/test structure for Maven or Gradle projects.", ["src/main/java/<package>/<Feature>Controller.java", "src/main/java/<package>/<Feature>Service.java", "src/test/java/<package>/<Feature>Test.java"], ["mvn test", "gradle test"]);
  }
  if (result.sql.files.length > 0) {
    addCapability("create-sql-migration", "Create SQL migrations with table/index/foreign-key awareness and verification hints.");
    addScaffoldRecommendation("SQL migration", "Create migration and rollback-friendly schema changes.", ["migrations/<timestamp>_<feature>.sql"], ["sqlfluff lint", "psql -f migrations/<timestamp>_<feature>.sql"]);
  }
  if (result.php.composerFiles.length > 0 || result.php.sourceFiles.length > 0) {
    addCapability("create-php-feature", "Create PHP/Laravel/Symfony features with routes, controllers, models, and PHPUnit tests.");
    addScaffoldRecommendation("PHP application feature", "Create controller/service/model/test structure.", ["app/Http/Controllers/<Feature>Controller.php", "app/Services/<Feature>Service.php", "tests/Feature/<Feature>Test.php"], ["composer test", "vendor/bin/phpunit"]);
  }
  if (result.kotlin.sourceFiles.length > 0 || result.kotlin.buildFiles.length > 0) {
    addCapability("create-kotlin-feature", "Create Kotlin Android/JVM/Ktor features with classes, routes/view models, and tests.");
    addScaffoldRecommendation("Kotlin feature", "Create source and test classes using detected Gradle layout.", ["src/main/kotlin/<package>/<Feature>.kt", "src/test/kotlin/<package>/<Feature>Test.kt"], ["./gradlew test"]);
  }
  if (result.swift.sourceFiles.length > 0 || result.swift.packageFiles.length > 0 || result.swift.projectFiles.length > 0) {
    addCapability("create-swift-feature", "Create Swift/SwiftUI features with models, views, services, and XCTest coverage.");
    addScaffoldRecommendation("Swift feature", "Create SwiftUI/view model/service/test structure.", ["Sources/<Module>/<Feature>View.swift", "Sources/<Module>/<Feature>Service.swift", "Tests/<Module>Tests/<Feature>Tests.swift"], ["swift test", "xcodebuild test"]);
  }
  if (result.scripts.shellFiles.length > 0 || result.scripts.powerShellFiles.length > 0) {
    addCapability("create-automation-script", "Create shell or PowerShell automation with safer command structure and verification steps.");
    addScaffoldRecommendation("Automation script", "Create build/test/release script with explicit parameters and dry-run friendly behavior.", ["scripts/<task>.ps1", "scripts/<task>.sh"], ["pwsh -File scripts/<task>.ps1", "bash scripts/<task>.sh"]);
  }
  if (result.r.sourceFiles.length > 0 || result.r.reportFiles.length > 0) {
    addCapability("create-r-analysis", "Create R analysis pipelines with data loading, functions, reports, and lightweight tests.");
    addScaffoldRecommendation("R analysis pipeline", "Create R script, report, and test structure.", ["R/<feature>.R", "reports/<feature>.qmd", "tests/testthat/test_<feature>.R"], ["Rscript -e \"testthat::test_dir('tests')\""]);
  }
}

detectCpp();
detectGo();
detectRust();
detectJava();
detectSql();
detectPhp();
detectKotlin();
detectSwift();
detectScripts();
detectR();
addGenerationGuidance();

console.log(JSON.stringify(result));
