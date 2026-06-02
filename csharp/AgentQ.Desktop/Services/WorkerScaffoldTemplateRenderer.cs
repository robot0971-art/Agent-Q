using System.IO;

namespace AgentQ.Desktop.Services;

public static class WorkerScaffoldTemplateRenderer
{
    public static string Render(
        WorkerPlan plan,
        string relativePath,
        WorkerScaffoldName feature,
        WorkerScaffoldContext? context = null)
    {
        context ??= new WorkerScaffoldContext();
        var path = relativePath.Replace('\\', '/');
        var language = plan.Language.ToLowerInvariant();
        var framework = plan.Framework.ToLowerInvariant();

        if (path.Equals("package.json", StringComparison.OrdinalIgnoreCase))
        {
            return RenderPackageJson(feature, framework, language);
        }

        if (path.Equals("index.html", StringComparison.OrdinalIgnoreCase))
        {
            return RenderIndexHtml(feature, language);
        }

        if (path.Equals("vite.config.ts", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("vite.config.js", StringComparison.OrdinalIgnoreCase))
        {
            return RenderViteConfig();
        }

        if (path.Equals("tsconfig.json", StringComparison.OrdinalIgnoreCase))
        {
            return RenderTsConfig();
        }

        if (path.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
        {
            return RenderCss(feature);
        }

        if (path.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".jsx", StringComparison.OrdinalIgnoreCase))
        {
            if (path.Equals("src/App.jsx", StringComparison.OrdinalIgnoreCase))
            {
                return RenderReactJavaScriptApp(feature);
            }

            if (path.Equals("src/main.jsx", StringComparison.OrdinalIgnoreCase))
            {
                return RenderReactJavaScriptMain();
            }

            return path.Contains(".test.", StringComparison.OrdinalIgnoreCase) ||
                   path.Contains(".spec.", StringComparison.OrdinalIgnoreCase)
                ? RenderJavaScriptTest(feature, context)
                : path.EndsWith(".jsx", StringComparison.OrdinalIgnoreCase)
                    ? RenderReactJavaScriptComponent(feature)
                    : RenderJavaScriptModule(feature);
        }

        if (path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase))
        {
            if (path.Equals("src/App.tsx", StringComparison.OrdinalIgnoreCase))
            {
                return RenderReactApp(feature);
            }

            if (path.Equals("src/main.tsx", StringComparison.OrdinalIgnoreCase))
            {
                return RenderReactMain();
            }

            return path.Contains(".test.", StringComparison.OrdinalIgnoreCase) ||
                   path.Contains(".spec.", StringComparison.OrdinalIgnoreCase)
                ? RenderTypeScriptTest(feature, context)
                : path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase)
                    ? RenderReactComponent(feature)
                    : RenderTypeScriptModule(feature);
        }

        if (path.Equals("requirements.txt", StringComparison.OrdinalIgnoreCase))
        {
            return RenderPythonRequirements(framework);
        }

        if (path.EndsWith(".py", StringComparison.OrdinalIgnoreCase))
        {
            if (path.Equals("tests/test_analyzer.py", StringComparison.OrdinalIgnoreCase))
            {
                return RenderPythonAnalyzerTest(feature);
            }

            if (path.Equals("tests/test_app.py", StringComparison.OrdinalIgnoreCase) &&
                framework.Contains("fastapi", StringComparison.OrdinalIgnoreCase))
            {
                return RenderFastApiTest(feature);
            }

            if (path.Equals("src/main.py", StringComparison.OrdinalIgnoreCase))
            {
                return RenderPythonCliMain(feature);
            }

            if (path.Equals("app/main.py", StringComparison.OrdinalIgnoreCase) &&
                framework.Contains("fastapi", StringComparison.OrdinalIgnoreCase))
            {
                return RenderFastApiMain();
            }

            if (path.Equals("app/routes.py", StringComparison.OrdinalIgnoreCase) &&
                framework.Contains("fastapi", StringComparison.OrdinalIgnoreCase))
            {
                return RenderFastApiRoutes(feature);
            }

            return path.Contains("test", StringComparison.OrdinalIgnoreCase)
                ? RenderPytest(feature)
                : framework.Contains("fastapi", StringComparison.OrdinalIgnoreCase)
                    ? RenderFastApiModule(feature)
                    : RenderPythonModule(feature);
        }

        if (path.EndsWith(".rs", StringComparison.OrdinalIgnoreCase))
        {
            return RenderRustModule(feature);
        }

        if (path.EndsWith(".go", StringComparison.OrdinalIgnoreCase))
        {
            return RenderGoModule(feature);
        }

        if (path.EndsWith(".java", StringComparison.OrdinalIgnoreCase))
        {
            return RenderJavaModule(path, feature);
        }

        if (path.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
        {
            return RenderSqlMigration(feature);
        }

        if (path.EndsWith(".php", StringComparison.OrdinalIgnoreCase))
        {
            return RenderPhpModule(path, feature);
        }

        if (path.EndsWith(".kt", StringComparison.OrdinalIgnoreCase))
        {
            return RenderKotlinModule(feature);
        }

        if (path.EndsWith(".swift", StringComparison.OrdinalIgnoreCase))
        {
            return RenderSwiftModule(path, feature);
        }

        if (path.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
        {
            return RenderPowerShellScript(feature);
        }

        if (path.EndsWith(".sh", StringComparison.OrdinalIgnoreCase))
        {
            return RenderShellScript(feature);
        }

        if (path.EndsWith(".R", StringComparison.OrdinalIgnoreCase) ||
            language == "r")
        {
            return RenderRModule(feature);
        }

        return $"# {Path.GetFileName(relativePath)}{Environment.NewLine}{Environment.NewLine}Scaffold for {feature.Pascal}.{Environment.NewLine}";
    }

    private static string RenderPackageJson(WorkerScaffoldName feature, string framework, string language)
    {
        var devScript = framework.Contains("vite", StringComparison.OrdinalIgnoreCase) ||
                        framework.Contains("react", StringComparison.OrdinalIgnoreCase)
            ? "vite --host 127.0.0.1"
            : "vite";
        var isTypeScript = language.Contains("typescript", StringComparison.OrdinalIgnoreCase);
        var buildScript = isTypeScript ? "tsc -b && vite build" : "vite build";
        var devDependencies = isTypeScript
            ? """
                "@types/react": "latest",
                "@types/react-dom": "latest",
                "@vitejs/plugin-react": "latest",
                "typescript": "latest",
                "vite": "latest",
                "vitest": "latest"
            """
            : """
                "@vitejs/plugin-react": "latest",
                "vite": "latest",
                "vitest": "latest"
            """;
        return
        $$"""
        {
          "name": "{{feature.Kebab}}",
          "version": "0.1.0",
          "private": true,
          "type": "module",
          "scripts": {
            "dev": "{{devScript}}",
            "build": "{{buildScript}}",
            "preview": "vite preview",
            "test": "vitest run"
          },
          "dependencies": {
            "react": "latest",
            "react-dom": "latest"
          },
          "devDependencies": {
        {{devDependencies}}
          }
        }
        """;
    }

    private static string RenderIndexHtml(WorkerScaffoldName feature, string language)
    {
        var entry = language.Contains("typescript", StringComparison.OrdinalIgnoreCase)
            ? "/src/main.tsx"
            : "/src/main.jsx";
        return
        $$"""
        <!doctype html>
        <html lang="en">
          <head>
            <meta charset="UTF-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1.0" />
            <title>{{feature.Pascal}}</title>
          </head>
          <body>
            <div id="root"></div>
            <script type="module" src="{{entry}}"></script>
          </body>
        </html>
        """;
    }

    private static string RenderViteConfig() =>
        """
        import { defineConfig } from "vite";
        import react from "@vitejs/plugin-react";

        export default defineConfig({
          plugins: [react()]
        });
        """;

    private static string RenderTsConfig() =>
        """
        {
          "compilerOptions": {
            "target": "ES2020",
            "useDefineForClassFields": true,
            "lib": ["DOM", "DOM.Iterable", "ES2020"],
            "allowJs": false,
            "skipLibCheck": true,
            "esModuleInterop": true,
            "allowSyntheticDefaultImports": true,
            "strict": true,
            "forceConsistentCasingInFileNames": true,
            "module": "ESNext",
            "moduleResolution": "Node",
            "resolveJsonModule": true,
            "isolatedModules": true,
            "noEmit": true,
            "jsx": "react-jsx"
          },
          "include": ["src"]
        }
        """;

    private static string RenderCss(WorkerScaffoldName feature) =>
        $$"""
        :root {
          color: #172033;
          background: #f7f8fb;
          font-family: Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
        }

        * {
          box-sizing: border-box;
        }

        body {
          margin: 0;
          min-width: 320px;
          min-height: 100vh;
        }

        button,
        input,
        textarea,
        select {
          font: inherit;
        }

        .app-shell {
          min-height: 100vh;
          display: grid;
          place-items: center;
          padding: 32px;
        }

        .app-panel {
          width: min(100%, 720px);
          border: 1px solid #d7deea;
          border-radius: 8px;
          background: #ffffff;
          padding: 28px;
          box-shadow: 0 18px 48px rgb(23 32 51 / 10%);
        }

        .app-panel h1 {
          margin: 0 0 10px;
          font-size: 32px;
          line-height: 1.15;
        }

        .app-panel p {
          margin: 0;
          color: #536179;
          line-height: 1.6;
        }
        """;

    private static string RenderReactApp(WorkerScaffoldName feature) =>
        $$"""
        import "./styles.css";

        export function App() {
          return (
            <main className="app-shell">
              <section className="app-panel" aria-labelledby="app-title">
                <h1 id="app-title">{{feature.Pascal}}</h1>
                <p>{{feature.Pascal}} is ready.</p>
              </section>
            </main>
          );
        }

        export default App;
        """;

    private static string RenderReactMain() =>
        """
        import { StrictMode } from "react";
        import { createRoot } from "react-dom/client";
        import App from "./App";

        createRoot(document.getElementById("root")!).render(
          <StrictMode>
            <App />
          </StrictMode>
        );
        """;

    private static string RenderReactComponent(WorkerScaffoldName feature) =>
        $$"""
        import { use{{feature.Pascal}} } from "./use{{feature.Pascal}}";

        export function {{feature.Pascal}}View() {
          const state = use{{feature.Pascal}}();

          return (
            <section aria-labelledby="{{feature.Kebab}}-title">
              <h2 id="{{feature.Kebab}}-title">{{feature.Pascal}}</h2>
              <p>{state.message}</p>
            </section>
          );
        }
        """;

    private static string RenderReactJavaScriptComponent(WorkerScaffoldName feature) =>
        $$"""
        import { use{{feature.Pascal}} } from "./use{{feature.Pascal}}";

        export function {{feature.Pascal}}View() {
          const state = use{{feature.Pascal}}();

          return (
            <section aria-labelledby="{{feature.Kebab}}-title">
              <h2 id="{{feature.Kebab}}-title">{{feature.Pascal}}</h2>
              <p>{state.message}</p>
            </section>
          );
        }
        """;

    private static string RenderReactJavaScriptApp(WorkerScaffoldName feature) =>
        $$"""
        import "./styles.css";

        export function App() {
          return (
            <main className="app-shell">
              <section className="app-panel" aria-labelledby="app-title">
                <h1 id="app-title">{{feature.Pascal}}</h1>
                <p>{{feature.Pascal}} is ready.</p>
              </section>
            </main>
          );
        }

        export default App;
        """;

    private static string RenderReactJavaScriptMain() =>
        """
        import { StrictMode } from "react";
        import { createRoot } from "react-dom/client";
        import App from "./App.jsx";

        createRoot(document.getElementById("root")).render(
          <StrictMode>
            <App />
          </StrictMode>
        );
        """;

    private static string RenderJavaScriptModule(WorkerScaffoldName feature) =>
        $$"""
        export function create{{feature.Pascal}}State() {
          return {
            message: "{{feature.Pascal}} is ready"
          };
        }

        export function use{{feature.Pascal}}() {
          return create{{feature.Pascal}}State();
        }
        """;

    private static string RenderJavaScriptTest(WorkerScaffoldName feature, WorkerScaffoldContext context)
    {
        var runner = context.UsesJest && !context.UsesVitest ? "@jest/globals" : "vitest";
        return
        $$"""
        import { describe, expect, it } from "{{runner}}";
        import { create{{feature.Pascal}}State } from "./use{{feature.Pascal}}";

        describe("{{feature.Pascal}}", () => {
          it("creates initial state", () => {
            expect(create{{feature.Pascal}}State().message).toContain("{{feature.Pascal}}");
          });
        });
        """;
    }

    private static string RenderTypeScriptModule(WorkerScaffoldName feature) =>
        $$"""
        export interface {{feature.Pascal}}State {
          message: string;
        }

        export function create{{feature.Pascal}}State(): {{feature.Pascal}}State {
          return {
            message: "{{feature.Pascal}} is ready"
          };
        }

        export function use{{feature.Pascal}}(): {{feature.Pascal}}State {
          return create{{feature.Pascal}}State();
        }
        """;

    private static string RenderTypeScriptTest(WorkerScaffoldName feature, WorkerScaffoldContext context)
    {
        var runner = context.UsesJest && !context.UsesVitest ? "@jest/globals" : "vitest";
        return
        $$"""
        import { describe, expect, it } from "{{runner}}";
        import { create{{feature.Pascal}}State } from "./use{{feature.Pascal}}";

        describe("{{feature.Pascal}}", () => {
          it("creates initial state", () => {
            expect(create{{feature.Pascal}}State().message).toContain("{{feature.Pascal}}");
          });
        });
        """;
    }

    private static string RenderFastApiModule(WorkerScaffoldName feature) =>
        $$"""
        from fastapi import APIRouter
        from pydantic import BaseModel

        router = APIRouter(prefix="/{{feature.Kebab}}", tags=["{{feature.Kebab}}"])


        class {{feature.Pascal}}Response(BaseModel):
            message: str


        @router.get("", response_model={{feature.Pascal}}Response)
        def get_{{feature.Snake}}() -> {{feature.Pascal}}Response:
            return {{feature.Pascal}}Response(message="{{feature.Pascal}} is ready")
        """;

    private static string RenderFastApiMain() =>
        """
        from fastapi import FastAPI

        from app.routes import router


        app = FastAPI()
        app.include_router(router)
        """;

    private static string RenderFastApiRoutes(WorkerScaffoldName feature) =>
        RenderFastApiModule(feature);

    private static string RenderPythonModule(WorkerScaffoldName feature) =>
        $$"""
        def create_{{feature.Snake}}_message() -> str:
            return "{{feature.Pascal}} is ready"
        """;

    private static string RenderPythonCliMain(WorkerScaffoldName feature) =>
        $$"""
        from src.analyzer import create_{{feature.Snake}}_message


        def main() -> None:
            print(create_{{feature.Snake}}_message())


        if __name__ == "__main__":
            main()
        """;

    private static string RenderPythonAnalyzerTest(WorkerScaffoldName feature) =>
        $$"""
        from src.analyzer import create_{{feature.Snake}}_message


        def test_{{feature.Snake}}_message() -> None:
            assert "{{feature.Pascal}}" in create_{{feature.Snake}}_message()
        """;

    private static string RenderFastApiTest(WorkerScaffoldName feature) =>
        $$"""
        from fastapi.testclient import TestClient

        from app.main import app


        def test_{{feature.Snake}}_route() -> None:
            client = TestClient(app)
            response = client.get("/{{feature.Kebab}}")
            assert response.status_code == 200
            assert response.json()["message"] == "{{feature.Pascal}} is ready"
        """;

    private static string RenderPythonRequirements(string framework)
    {
        if (framework.Contains("fastapi", StringComparison.OrdinalIgnoreCase))
        {
            return
                """
                fastapi
                httpx
                pytest
                """;
        }

        if (framework.Contains("streamlit", StringComparison.OrdinalIgnoreCase))
        {
            return
                """
                pandas
                streamlit
                """;
        }

        return
            """
            pandas
            pytest
            """;
    }

    private static string RenderPytest(WorkerScaffoldName feature) =>
        $$"""
        from app.{{feature.Snake}} import create_{{feature.Snake}}_message


        def test_{{feature.Snake}}_message() -> None:
            assert "{{feature.Pascal}}" in create_{{feature.Snake}}_message()
        """;

    private static string RenderRustModule(WorkerScaffoldName feature) =>
        $$"""
        pub struct {{feature.Pascal}} {
            message: String,
        }

        impl {{feature.Pascal}} {
            pub fn new() -> Self {
                Self {
                    message: "{{feature.Pascal}} is ready".to_string(),
                }
            }

            pub fn message(&self) -> &str {
                &self.message
            }
        }

        #[cfg(test)]
        mod tests {
            use super::*;

            #[test]
            fn creates_message() {
                assert!({{feature.Pascal}}::new().message().contains("{{feature.Pascal}}"));
            }
        }
        """;

    private static string RenderGoModule(WorkerScaffoldName feature) =>
        $$"""
        package {{feature.Snake}}

        func Message() string {
        	return "{{feature.Pascal}} is ready"
        }
        """;

    private static string RenderJavaModule(string path, WorkerScaffoldName feature) =>
        $$"""
        package app;

        public final class {{Path.GetFileNameWithoutExtension(path)}} {
            public String message() {
                return "{{feature.Pascal}} is ready";
            }
        }
        """;

    private static string RenderSqlMigration(WorkerScaffoldName feature) =>
        $$"""
        create table if not exists {{feature.Snake}} (
            id bigserial primary key,
            name text not null,
            created_at timestamptz not null default now()
        );
        """;

    private static string RenderPhpModule(string path, WorkerScaffoldName feature) =>
        $$"""
        <?php

        final class {{Path.GetFileNameWithoutExtension(path)}}
        {
            public function message(): string
            {
                return '{{feature.Pascal}} is ready';
            }
        }
        """;

    private static string RenderKotlinModule(WorkerScaffoldName feature) =>
        $$"""
        package app

        class {{feature.Pascal}} {
            fun message(): String = "{{feature.Pascal}} is ready"
        }
        """;

    private static string RenderSwiftModule(string path, WorkerScaffoldName feature) =>
        $$"""
        import Foundation

        struct {{Path.GetFileNameWithoutExtension(path)}} {
            let message = "{{feature.Pascal}} is ready"
        }
        """;

    private static string RenderPowerShellScript(WorkerScaffoldName feature) =>
        $$"""
        param(
            [switch]$DryRun
        )

        Set-StrictMode -Version Latest
        $ErrorActionPreference = "Stop"

        Write-Host "{{feature.Pascal}} task ready. DryRun=$DryRun"
        """;

    private static string RenderShellScript(WorkerScaffoldName feature) =>
        $$"""
        #!/usr/bin/env bash
        set -euo pipefail

        echo "{{feature.Pascal}} task ready"
        """;

    private static string RenderRModule(WorkerScaffoldName feature) =>
        $$"""
        create_{{feature.Snake}}_summary <- function(data) {
          list(
            name = "{{feature.Pascal}}",
            rows = nrow(data)
          )
        }
        """;
}
