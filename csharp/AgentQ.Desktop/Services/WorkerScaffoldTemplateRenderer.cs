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

        if (path.Equals("CMakeLists.txt", StringComparison.OrdinalIgnoreCase))
        {
            return RenderCMakeLists(feature);
        }

        if (path.Equals("go.mod", StringComparison.OrdinalIgnoreCase))
        {
            return RenderGoMod(feature);
        }

        if (path.Equals("Cargo.toml", StringComparison.OrdinalIgnoreCase))
        {
            return RenderCargoToml(feature);
        }

        if (path.Equals("pom.xml", StringComparison.OrdinalIgnoreCase))
        {
            return RenderPomXml(feature);
        }

        if (path.Equals("composer.json", StringComparison.OrdinalIgnoreCase))
        {
            return RenderComposerJson(feature);
        }

        if (path.Equals("settings.gradle.kts", StringComparison.OrdinalIgnoreCase))
        {
            return RenderGradleSettings(feature);
        }

        if (path.Equals("build.gradle.kts", StringComparison.OrdinalIgnoreCase))
        {
            return RenderGradleBuild();
        }

        if (path.Equals("Package.swift", StringComparison.OrdinalIgnoreCase))
        {
            return RenderSwiftPackage(feature);
        }

        if (path.Equals("DESCRIPTION", StringComparison.OrdinalIgnoreCase))
        {
            return RenderRDescription(feature);
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

        if (path.EndsWith(".cpp", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".cc", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".cxx", StringComparison.OrdinalIgnoreCase))
        {
            return RenderCppSource(path, feature);
        }

        if (path.EndsWith(".hpp", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".h", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".hh", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".hxx", StringComparison.OrdinalIgnoreCase))
        {
            return RenderCppHeader(feature);
        }

        if (path.EndsWith(".rs", StringComparison.OrdinalIgnoreCase))
        {
            return RenderRustModule(path, feature);
        }

        if (path.EndsWith(".go", StringComparison.OrdinalIgnoreCase))
        {
            return RenderGoModule(path, feature);
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

        if (path.EndsWith(".kt", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".kts", StringComparison.OrdinalIgnoreCase))
        {
            return RenderKotlinModule(path, feature);
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

    private static string RenderCMakeLists(WorkerScaffoldName feature) =>
        $$"""
        cmake_minimum_required(VERSION 3.20)
        project({{feature.Camel}} LANGUAGES CXX)

        set(CMAKE_CXX_STANDARD 20)
        set(CMAKE_CXX_STANDARD_REQUIRED ON)

        add_library({{feature.Camel}} src/app.cpp)
        target_include_directories({{feature.Camel}} PUBLIC include)

        add_executable({{feature.Camel}}_cli src/main.cpp)
        target_link_libraries({{feature.Camel}}_cli PRIVATE {{feature.Camel}})
        """;

    private static string RenderCppHeader(WorkerScaffoldName feature) =>
        $$"""
        #pragma once

        #include <string>

        namespace app {

        std::string {{feature.Camel}}Message();

        }  // namespace app
        """;

    private static string RenderCppSource(string path, WorkerScaffoldName feature)
    {
        if (path.Equals("src/main.cpp", StringComparison.OrdinalIgnoreCase))
        {
            return
            $$"""
            #include <iostream>

            #include "app/app.hpp"

            int main() {
                std::cout << app::{{feature.Camel}}Message() << '\n';
                return 0;
            }
            """;
        }

        if (path.Contains("test", StringComparison.OrdinalIgnoreCase))
        {
            return
            $$"""
            #include "app/app.hpp"

            int main() {
                return app::{{feature.Camel}}Message().empty() ? 1 : 0;
            }
            """;
        }

        return
        $$"""
        #include "app/app.hpp"

        namespace app {

        std::string {{feature.Camel}}Message() {
            return "{{feature.Pascal}} is ready";
        }

        }  // namespace app
        """;
    }

    private static string RenderGoMod(WorkerScaffoldName feature) =>
        $$"""
        module example.com/{{feature.Kebab}}

        go 1.22
        """;

    private static string RenderCargoToml(WorkerScaffoldName feature) =>
        $$"""
        [package]
        name = "{{feature.Kebab}}"
        version = "0.1.0"
        edition = "2021"

        [dependencies]
        """;

    private static string RenderPomXml(WorkerScaffoldName feature) =>
        $$"""
        <project xmlns="http://maven.apache.org/POM/4.0.0"
                 xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                 xsi:schemaLocation="http://maven.apache.org/POM/4.0.0 https://maven.apache.org/xsd/maven-4.0.0.xsd">
          <modelVersion>4.0.0</modelVersion>
          <groupId>app</groupId>
          <artifactId>{{feature.Kebab}}</artifactId>
          <version>0.1.0</version>
          <properties>
            <maven.compiler.release>17</maven.compiler.release>
            <project.build.sourceEncoding>UTF-8</project.build.sourceEncoding>
          </properties>
          <dependencies>
            <dependency>
              <groupId>org.junit.jupiter</groupId>
              <artifactId>junit-jupiter</artifactId>
              <version>5.10.2</version>
              <scope>test</scope>
            </dependency>
          </dependencies>
          <build>
            <plugins>
              <plugin>
                <groupId>org.apache.maven.plugins</groupId>
                <artifactId>maven-surefire-plugin</artifactId>
                <version>3.2.5</version>
              </plugin>
            </plugins>
          </build>
        </project>
        """;

    private static string RenderComposerJson(WorkerScaffoldName feature) =>
        $$"""
        {
          "name": "agentq/{{feature.Kebab}}",
          "type": "project",
          "autoload": {
            "psr-4": {
              "App\\": "src/"
            }
          },
          "autoload-dev": {
            "psr-4": {
              "Tests\\": "tests/"
            }
          },
          "require-dev": {
            "phpunit/phpunit": "^11.0"
          },
          "scripts": {
            "test": "phpunit"
          }
        }
        """;

    private static string RenderGradleSettings(WorkerScaffoldName feature) =>
        $$"""
        pluginManagement {
            repositories {
                gradlePluginPortal()
                mavenCentral()
            }
        }

        dependencyResolutionManagement {
            repositoriesMode.set(RepositoriesMode.FAIL_ON_PROJECT_REPOS)
            repositories {
                mavenCentral()
            }
        }

        rootProject.name = "{{feature.Kebab}}"
        """;

    private static string RenderGradleBuild() =>
        """
        plugins {
            kotlin("jvm") version "1.9.24"
        }

        dependencies {
            testImplementation(kotlin("test"))
        }

        tasks.test {
            useJUnitPlatform()
        }
        """;

    private static string RenderSwiftPackage(WorkerScaffoldName feature) =>
        $$"""
        // swift-tools-version: 5.10
        import PackageDescription

        let package = Package(
            name: "{{feature.Pascal}}",
            products: [
                .library(name: "{{feature.Pascal}}", targets: ["App"])
            ],
            targets: [
                .target(name: "App"),
                .testTarget(name: "AppTests", dependencies: ["App"])
            ]
        )
        """;

    private static string RenderRDescription(WorkerScaffoldName feature) =>
        $$"""
        Package: {{feature.Camel}}
        Type: Package
        Title: {{feature.Pascal}}
        Version: 0.1.0
        Encoding: UTF-8
        Depends:
            R (>= 4.1)
        Suggests:
            testthat
        """;

    private static string RenderRustModule(string path, WorkerScaffoldName feature)
    {
        if (path.Equals("src/main.rs", StringComparison.OrdinalIgnoreCase))
        {
            return
            $$"""
            fn main() {
                println!("{}", {{feature.Snake}}::{{feature.Snake}}_message());
            }
            """;
        }

        if (path.Equals("src/lib.rs", StringComparison.OrdinalIgnoreCase))
        {
            return
            $$"""
            pub fn {{feature.Snake}}_message() -> &'static str {
                "{{feature.Pascal}} is ready"
            }

            #[cfg(test)]
            mod tests {
                use super::*;

                #[test]
                fn creates_message() {
                    assert!({{feature.Snake}}_message().contains("{{feature.Pascal}}"));
                }
            }
            """;
        }

        return
        $$"""
        #[test]
        fn {{feature.Snake}}_integration_smoke() {
            assert!({{feature.Snake}}::{{feature.Snake}}_message().contains("{{feature.Pascal}}"));
        }
        """;
    }

    private static string RenderGoModule(string path, WorkerScaffoldName feature)
    {
        if (path.EndsWith("main.go", StringComparison.OrdinalIgnoreCase))
        {
            return
            $$"""
            package main

            import (
                "fmt"

                "example.com/{{feature.Kebab}}/internal/app"
            )

            func main() {
                fmt.Println(app.Message())
            }
            """;
        }

        if (path.EndsWith("_test.go", StringComparison.OrdinalIgnoreCase))
        {
            return
            $$"""
            package app

            import "testing"

            func TestMessage(t *testing.T) {
                if Message() == "" {
                    t.Fatal("message should not be empty")
                }
            }
            """;
        }

        return
        $$"""
        package app

        func Message() string {
        	return "{{feature.Pascal}} is ready"
        }
        """;
    }

    private static string RenderJavaModule(string path, WorkerScaffoldName feature) =>
        path.Contains("test", StringComparison.OrdinalIgnoreCase)
            ?
        $$"""
        package app;

        import org.junit.jupiter.api.Test;

        import static org.junit.jupiter.api.Assertions.assertTrue;

        public final class {{Path.GetFileNameWithoutExtension(path)}} {
            @Test
            void createsMessage() {
                assertTrue(new App().message().contains("{{feature.Pascal}}"));
            }
        }
        """
            :
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
        path.Contains("test", StringComparison.OrdinalIgnoreCase)
            ?
        $$"""
        <?php

        namespace Tests;

        use App\App;
        use PHPUnit\Framework\TestCase;

        final class {{Path.GetFileNameWithoutExtension(path)}} extends TestCase
        {
            public function testMessage(): void
            {
                $this->assertStringContainsString('{{feature.Pascal}}', (new App())->message());
            }
        }
        """
            :
        $$"""
        <?php

        namespace App;

        final class {{Path.GetFileNameWithoutExtension(path)}}
        {
            public function message(): string
            {
                return '{{feature.Pascal}} is ready';
            }
        }
        """;

    private static string RenderKotlinModule(string path, WorkerScaffoldName feature) =>
        path.Contains("test", StringComparison.OrdinalIgnoreCase)
            ?
        $$"""
        package app

        import kotlin.test.Test
        import kotlin.test.assertTrue

        class {{feature.Pascal}}Test {
            @Test
            fun createsMessage() {
                assertTrue({{feature.Pascal}}().message().contains("{{feature.Pascal}}"))
            }
        }
        """
            :
        $$"""
        package app

        class {{feature.Pascal}} {
            fun message(): String = "{{feature.Pascal}} is ready"
        }
        """;

    private static string RenderSwiftModule(string path, WorkerScaffoldName feature) =>
        path.Contains("Tests", StringComparison.OrdinalIgnoreCase)
            ?
        $$"""
        import XCTest
        @testable import App

        final class {{Path.GetFileNameWithoutExtension(path)}}: XCTestCase {
            func testMessage() {
                XCTAssertTrue(AppFeature().message.contains("{{feature.Pascal}}"))
            }
        }
        """
            :
        $$"""
        import Foundation

        public struct AppFeature {
            public init() {}

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
