# faxe

Flow-based data collector / data processor — **.NET 10**.

Faxe collects data from various sources, processes and routes it through DFS graphs, and can store data to various backends.

API and DFS behaviour follow the classic Faxe design (see [docs](https://heyoka.github.io/faxe-docs/site) and [HTTP API](https://heyoka.github.io/faxe-docs/site/faxe_rest_api.html)).

## Requirements

- .NET 10 SDK

## Build

```bash
dotnet build Faxe.slnx
dotnet test Faxe.slnx
```

## Run

```bash
dotnet run --project src/Faxe.Api
```

- Swagger UI: `/swagger`
- REST API: `/v1/...`
- Default: anonymous auth enabled (`Faxe:AllowAnonymous=true`). Seeded user `admin`/`admin` when auth is required.

## Solution layout

| Project | Role |
|---------|------|
| `Faxe.Core` | `data_point` / `data_batch`, field paths, time helpers |
| `Faxe.Dfs` | DFS lexer/parser/compiler + lambda evaluator |
| `Faxe.Flow` | Graph runtime and node host |
| `Faxe.Nodes` | DFS node implementations |
| `Faxe.Persistence` | SQLite store (tasks, templates, users) |
| `Faxe.Api` | ASP.NET Core REST API + Swagger |

## DFS

Scripts in `dfs/` are sample pipelines. Example:

```dfs
|value_emitter()
.every(3s)
.mode('monotonic_int')
.type(point)
|where()
.lambda(lambda: "val" > 3)
|debug()
```

```bash
curl -X POST http://localhost:5000/v1/task/register \
  -F name=demo \
  -F dfs='|value_emitter().every(1s)|debug()'
curl http://localhost:5000/v1/task/start/demo
```

## Docker

```bash
docker build -t faxe .
docker run --rm -p 8080:8080 faxe
```
