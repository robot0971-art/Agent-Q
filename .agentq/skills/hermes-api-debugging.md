---
id: hermes-api-debugging
title: Hermes API Debugging Adaptation
priority: 60
taskKinds: bug-fix,verification-failure,analysis
triggers: api,http,provider,request,response,auth,token,endpoint,streaming,API,인증,토큰,요청,응답,스트리밍
excludes: ui-only,문서만
---
# API Debugging

Use this skill when AgentQ has a provider, HTTP, auth, streaming, or endpoint failure.

## Procedure

1. Identify the failing boundary: config loading, request construction, transport, response parsing, streaming, or UI callback.
2. Redact secrets before logging or summarizing anything.
3. Capture the smallest useful evidence: status code, provider name, model, endpoint shape, and first meaningful error.
4. Compare the failing provider path with a known-good provider or mock service path.
5. Fix the smallest boundary. Avoid broad provider rewrites.
6. Run a focused provider/tool test when possible.

## AgentQ Notes

- Prefer tests that use `StubHttpClientFactory` or mock provider responses.
- Do not paste API keys, bearer tokens, or full auth headers.
- For OpenAI-compatible providers, separate base URL, model name, auth header, request body, and streaming parser checks.
- For desktop failures, also inspect `DesktopProviderFailureClassifier` and callback handling.
