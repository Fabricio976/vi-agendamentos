# Vi Agendamentos: API

API do app de agendamento para salões.

## Ambiente local

O front fica no repositório vizinho `vi-agendamentos-web`. Os dois precisam estar lado a lado:

```
C:\Dev\desafio\
├── vi-agendamentos\
└── vi-agendamentos-web\
```

Com o Docker Desktop aberto, na raiz deste repositório, crie o `.env` na primeira vez e suba:

```powershell
Copy-Item .env.example .env
docker compose up --build
```

- Front: http://localhost:4200
- API: http://localhost:5080/api/health
- Banco: http://localhost:5080/api/health/db responde `Healthy` quando a API alcança o Postgres

As portas ficam só nesta máquina, em `127.0.0.1`. API e front recarregam ao salvar. Para parar, use `docker compose down`.

A API roda no contêiner, que recebe a string de conexão do compose. Fora do Docker, ela exige a variável `ConnectionStrings__Default` e não sobe sem ela.

## Banco local

O compose sobe um Postgres 17, a mesma versão dos projetos novos do Supabase, em `127.0.0.1:5432`. Banco, usuário e senha vêm do `.env`, que fica fora do git; o `.env.example` traz os valores do ambiente local. Sem o `.env`, o compose para com erro. Os dados ficam no volume `db-data` e sobrevivem ao `docker compose down`. O `docker compose down -v` apaga o banco.

As tabelas nascem das migrations do EF Core. O `dotnet ef` roda dentro do contêiner da API, onde está a string de conexão. Para aplicar as migrations, com o compose no ar:

```powershell
docker compose exec api dotnet ef database update --project ViAgendamentos.Api
```

Para criar uma migration depois de mudar o modelo:

```powershell
docker compose exec api dotnet ef migrations add NomeDaMudanca --project ViAgendamentos.Api --output-dir Data/Migrations
```

## Testes

Rodam no Windows. Pré-requisitos: SDK do .NET 10 e o Docker Desktop aberto, porque os testes do banco sobem um Postgres 17 descartável pelo Testcontainers.

```powershell
dotnet test
```
