# Vi Agendamentos: API

API do app de agendamento para salões.

## Ambiente local

O front fica no repositório vizinho `vi-agendamentos-web`. Os dois precisam estar lado a lado:

```
C:\Dev\desafio\
├── vi-agendamentos\
└── vi-agendamentos-web\
```

Com o Docker Desktop aberto, na raiz deste repositório:

```powershell
docker compose up --build
```

- Front: http://localhost:4200
- API: http://localhost:5080/api/health

Os dois recarregam ao salvar. Para parar, use `docker compose down`.

## Testes

Rodam no Windows, sem contêiner. Pré-requisito: SDK do .NET 10.

```powershell
dotnet test
```
