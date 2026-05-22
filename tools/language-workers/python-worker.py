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
    "symbols": [],
    "fastapiRoutes": [],
    "sqlalchemyModels": [],
    "pytestTargets": [],
    "projectMap": [],
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


def analyze_python_file(path):
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
    for node in ast.walk(tree):
        if isinstance(node, ast.Import):
            for alias in node.names:
                result["imports"].append({"path": relative, "line": node.lineno, "module": alias.name})
        elif isinstance(node, ast.ImportFrom):
            result["imports"].append({"path": relative, "line": node.lineno, "module": node.module or ""})
        elif isinstance(node, ast.ClassDef):
            result["symbols"].append({"path": relative, "line": node.lineno, "kind": "class", "name": node.name, "language": "Python"})
            if is_sqlalchemy_model(node):
                result["sqlalchemyModels"].append({"path": relative, "line": node.lineno, "name": node.name})
        elif isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)):
            result["symbols"].append({"path": relative, "line": node.lineno, "kind": "function", "name": node.name, "language": "Python"})
            route = fastapi_route(node)
            if route:
                result["fastapiRoutes"].append({"path": relative, "line": node.lineno, "name": node.name, **route})

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


def name_of(node):
    if isinstance(node, ast.Name):
        return node.id
    if isinstance(node, ast.Attribute):
        parent = name_of(node.value)
        return f"{parent}.{node.attr}" if parent else node.attr
    return ""


def main():
    for path in find_named("pyproject.toml"):
        analyze_pyproject(path)
        add_project_map("Python package", os.path.dirname(rel(path)) or ".")

    for path in find_named("requirements.txt"):
        analyze_requirements(path)
        add_project_map("Python package", os.path.dirname(rel(path)) or ".")

    for path in walk_python_files():
        analyze_python_file(path)

    if result["fastapiRoutes"]:
        add_project_map("FastAPI routes", os.path.dirname(result["fastapiRoutes"][0]["path"]))
    if result["sqlalchemyModels"]:
        add_project_map("SQLAlchemy models", os.path.dirname(result["sqlalchemyModels"][0]["path"]))
    if result["pytestTargets"]:
        add_project_map("Pytest tests", os.path.dirname(result["pytestTargets"][0]["path"]))

    print(json.dumps(result))


if __name__ == "__main__":
    main()
