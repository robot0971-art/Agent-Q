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

        if (path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase))
        {
            return path.Contains(".test.", StringComparison.OrdinalIgnoreCase) ||
                   path.Contains(".spec.", StringComparison.OrdinalIgnoreCase)
                ? RenderTypeScriptTest(feature, context)
                : path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase)
                    ? RenderReactComponent(feature)
                    : RenderTypeScriptModule(feature);
        }

        if (path.EndsWith(".py", StringComparison.OrdinalIgnoreCase))
        {
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

    private static string RenderPythonModule(WorkerScaffoldName feature) =>
        $$"""
        def create_{{feature.Snake}}_message() -> str:
            return "{{feature.Pascal}} is ready"
        """;

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
