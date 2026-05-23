import ast
import json
import os
import sys

try:
    import tomllib
except Exception:
    tomllib = None

ROOT = os.path.abspath(sys.argv[1] if len(sys.argv) > 1 else os.getcwd())
MAX_FILES = 600
IGNORED = {
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
    ".mypy_cache",
    ".pytest_cache",
}

result = {
    "worker": "python-worker",
    "version": 1,
    "root": ROOT,
    "pyprojects": [],
    "requirements": [],
    "imports": [],
    "callSites": [],
    "symbols": [],
    "fastapiRoutes": [],
    "sqlalchemyModels": [],
    "pytestTargets": [],
    "projectMap": [],
    "failureHints": [],
    "warnings": [],
}


def rel(path):
    return os.path.relpath(path, ROOT).replace(os.sep, "/")


def walk_python_files():
    files = []
    for current, dirs, names in os.walk(ROOT):
        dirs[:] = [name for name in dirs if name not in IGNORED]
        for name in names:
            if name.endswith(".py"):
                files.append(os.path.join(current, name))
                if len(files) >= MAX_FILES:
                    return files
    return files


def find_named(name):
    matches = []
    for current, dirs, names in os.walk(ROOT):
        dirs[:] = [item for item in dirs if item not in IGNORED]
        if name in names:
            matches.append(os.path.join(current, name))
    return matches


def add_project_map(role, path):
    if not path:
        return
    entry = {"role": role, "path": path}
    if entry not in result["projectMap"]:
        result["projectMap"].append(entry)


def analyze_pyproject(path):
    data = {}
    if tomllib is not None:
        try:
            with open(path, "rb") as handle:
                data = tomllib.load(handle)
        except Exception:
            result["warnings"].append(f"Could not parse {rel(path)}")

    project = data.get("project", {}) if isinstance(data, dict) else {}
    dependencies = project.get("dependencies", []) if isinstance(project, dict) else []
    result["pyprojects"].append({
        "path": rel(path),
        "name": project.get("name", "") if isinstance(project, dict) else "",
        "dependencies": [str(item) for item in dependencies],
    })


def analyze_requirements(path):
    dependencies = []
    try:
        with open(path, "r", encoding="utf-8") as handle:
            for line in handle:
                text = line.strip()
                if text and not text.startswith("#"):
                    dependencies.append(text)
    except Exception:
        result["warnings"].append(f"Could not read {rel(path)}")

    result["requirements"].append({"path": rel(path), "dependencies": dependencies})


def build_module_index(files):
    index = {}
    for path in files:
        relative = rel(path)
        if relative.endswith("/__init__.py"):
            module = relative[:-12].replace("/", ".")
        else:
            module = relative[:-3].replace("/", ".")

        if module:
            index[module] = relative

        parts = module.split(".")
        for offset in range(1, len(parts)):
            suffix = ".".join(parts[offset:])
            if suffix and suffix not in index:
                index[suffix] = relative

    return index


def analyze_python_file(path, module_index):
    try:
        with open(path, "r", encoding="utf-8") as handle:
            text = handle.read()
    except Exception:
        return

    try:
        tree = ast.parse(text, filename=path)
    except SyntaxError as exc:
        result["warnings"].append(f"Syntax error in {rel(path)}:{exc.lineno or 0}")
        return

    relative = rel(path)
    parents = {}
    for parent in ast.walk(tree):
        for child in ast.iter_child_nodes(parent):
            parents[child] = parent

    for node in ast.walk(tree):
        if isinstance(node, ast.Import):
            for alias in node.names:
                result["imports"].append({
                    "path": relative,
                    "line": node.lineno,
                    "module": alias.name,
                    "importedName": alias.asname or alias.name,
                    "resolvedPath": resolve_import(relative, alias.name, 0, module_index),
                })
        elif isinstance(node, ast.ImportFrom):
            module = node.module or ""
            result["imports"].append({
                "path": relative,
                "line": node.lineno,
                "module": module,
                "importedName": ", ".join(alias.name for alias in node.names),
                "level": node.level,
                "resolvedPath": resolve_import(relative, module, node.level, module_index),
            })
        elif isinstance(node, ast.ClassDef):
            result["symbols"].append({"path": relative, "line": node.lineno, "kind": "class", "name": node.name, "language": "Python"})
            if is_sqlalchemy_model(node):
                result["sqlalchemyModels"].append({"path": relative, "line": node.lineno, "name": node.name})
            if is_pytest_class(node):
                result["pytestTargets"].append({"path": relative, "line": node.lineno, "kind": "test-class", "name": node.name})
        elif isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)):
            result["symbols"].append({"path": relative, "line": node.lineno, "kind": "function", "name": node.name, "language": "Python"})
            route = fastapi_route(node)
            if route:
                result["fastapiRoutes"].append({"path": relative, "line": node.lineno, "name": node.name, **route})
            if is_pytest_function(node):
                result["pytestTargets"].append({"path": relative, "line": node.lineno, "kind": "test-function", "name": node.name})
        elif isinstance(node, ast.Call):
            call_name = name_of(node.func)
            if call_name:
                result["callSites"].append({
                    "path": relative,
                    "line": getattr(node, "lineno", 0),
                    "name": call_name,
                    "enclosingSymbol": enclosing_symbol(node, parents),
                })

    if relative.startswith("tests/") or "/tests/" in relative or os.path.basename(relative).startswith("test_") or relative.endswith("_test.py"):
        result["pytestTargets"].append({"path": relative, "kind": "test-file"})


def is_sqlalchemy_model(node):
    for base in node.bases:
        text = name_of(base)
        if text in {"Base", "DeclarativeBase"} or text.endswith(".Base"):
            return True
    for item in node.body:
        if isinstance(item, ast.Assign):
            for target in item.targets:
                if isinstance(target, ast.Name) and target.id == "__tablename__":
                    return True
    return False


def fastapi_route(node):
    for decorator in node.decorator_list:
        call = decorator if isinstance(decorator, ast.Call) else None
        target = call.func if call else decorator
        name = name_of(target)
        if not name:
            continue
        method = name.rsplit(".", 1)[-1]
        if method not in {"get", "post", "put", "patch", "delete", "head", "options"}:
            continue
        route_path = ""
        if call and call.args and isinstance(call.args[0], ast.Constant):
            route_path = str(call.args[0].value)
        return {"method": method.upper(), "route": route_path}
    return None


def is_pytest_function(node):
    if node.name.startswith("test_"):
        return True
    for decorator in node.decorator_list:
        name = name_of(decorator.func if isinstance(decorator, ast.Call) else decorator)
        if name.startswith("pytest.") or name in {"fixture", "pytest.fixture"}:
            return True
    return False


def is_pytest_class(node):
    return node.name.startswith("Test")


def enclosing_symbol(node, parents):
    current = parents.get(node)
    while current:
        if isinstance(current, (ast.FunctionDef, ast.AsyncFunctionDef, ast.ClassDef)):
            return current.name
        current = parents.get(current)
    return ""


def name_of(node):
    if isinstance(node, ast.Name):
        return node.id
    if isinstance(node, ast.Attribute):
        parent = name_of(node.value)
        return f"{parent}.{node.attr}" if parent else node.attr
    return ""


def resolve_import(relative_path, module, level, module_index):
    normalized = (module or "").strip(".")
    candidates = []

    if level > 0:
        package_parts = relative_path[:-3].split("/")[:-1]
        drop = max(level - 1, 0)
        if drop:
            package_parts = package_parts[:-drop]
        base = ".".join(package_parts)
        if normalized:
            candidates.append(f"{base}.{normalized}" if base else normalized)
        elif base:
            candidates.append(base)
    elif normalized:
        candidates.append(normalized)

    if normalized:
        parts = normalized.split(".")
        for index in range(len(parts) - 1, 0, -1):
            candidates.append(".".join(parts[:index]))

    for candidate in candidates:
        if candidate in module_index:
            return module_index[candidate]

    return ""


def add_failure_hints():
    dependencies = "\n".join(
        dep
        for group in result["requirements"]
        for dep in group.get("dependencies", [])
    )
    dependencies += "\n" + "\n".join(
        dep
        for group in result["pyprojects"]
        for dep in group.get("dependencies", [])
    )
    lowered = dependencies.lower()

    if "pytest" in lowered and not result["pytestTargets"]:
        result["failureHints"].append("pytest dependency detected but no pytest targets were found.")
    if result["pytestTargets"] and "pytest" not in lowered:
        result["failureHints"].append("pytest-style tests detected without an explicit pytest dependency.")
    if result["pyprojects"] and tomllib is None:
        result["failureHints"].append("pyproject.toml files were found but tomllib is unavailable in this Python runtime.")


def main():
    for path in find_named("pyproject.toml"):
        analyze_pyproject(path)
        add_project_map("Python package", os.path.dirname(rel(path)) or ".")

    for path in find_named("requirements.txt"):
        analyze_requirements(path)
        add_project_map("Python package", os.path.dirname(rel(path)) or ".")

    python_files = walk_python_files()
    module_index = build_module_index(python_files)
    for path in python_files:
        analyze_python_file(path, module_index)

    add_failure_hints()

    if result["fastapiRoutes"]:
        add_project_map("FastAPI routes", os.path.dirname(result["fastapiRoutes"][0]["path"]))
    if result["sqlalchemyModels"]:
        add_project_map("SQLAlchemy models", os.path.dirname(result["sqlalchemyModels"][0]["path"]))
    if result["pytestTargets"]:
        add_project_map("Pytest tests", os.path.dirname(result["pytestTargets"][0]["path"]))

    print(json.dumps(result))


if __name__ == "__main__":
    main()
