# Vi Agendamentos: API

API do app de agendamento para salões.

## Ambiente local

O front fica no repositório vizinho `vi-agendamentos-web`. Os dois precisam estar lado a lado:

```
C:\Dev\desafio\
├── vi-agendamentos\
└── vi-agendamentos-web\
```

Com o Docker Desktop aberto, na raiz deste repositório, crie o `.env` na primeira vez, preencha nele as credenciais do Google (ver [Login com Google](#login-com-google)) e suba:

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

## Login com Google

O login usa um cliente OAuth do Google, criado no projeto `vi-agendamentos` do Google Cloud, em **Google Auth Platform > Clients**. O cliente do ambiente local tem o endereço de retorno `http://localhost:4200/api/signin-google`. Enquanto o app está em modo de teste, só entram as contas listadas em **Audience > Test users**.

O Client ID e o Client secret vão no `.env`, em `GOOGLE_CLIENT_ID` e `GOOGLE_CLIENT_SECRET`. Sem eles, o compose para com erro, e a API não sobe.

Abra o front por `http://localhost:4200`, e não por `127.0.0.1`: o Google só devolve a pessoa para o endereço cadastrado.

## Google Agenda

A profissional conecta a própria agenda pelo mesmo cliente OAuth do login, com o escopo `calendar.events.owned`. No projeto do Google Cloud:

- a Google Calendar API fica ativada;
- o cliente OAuth do ambiente local tem também o endereço de retorno `http://localhost:4200/api/google/agenda/callback`, entre as URIs de redirecionamento, e não entre as origens JavaScript;
- em modo de teste, só conectam as contas de **Audience > Test users**, e o Google invalida o refresh token em 7 dias. Com o app "Em produção", o refresh token não vence, e a tela "app não verificado" aparece uma vez para quem conecta.

A API guarda só o refresh token, cifrado com AES-256-GCM. A chave vai no `.env`, em `GOOGLE_AGENDA_CHAVE`: 32 bytes em base64, gerados no PowerShell com

```powershell
$bytes = New-Object byte[] 32; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes); [Convert]::ToBase64String($bytes)
```

Sem a chave, ou com outro tamanho, a API não sobe. Trocar a chave invalida as conexões gravadas: cada profissional conecta de novo.

Para testar a conexão com a profissional logada, abra `http://localhost:4200/api/saloes/<salão>/profissionais/<profissional>/google/conectar`, autorize, e rode no console do navegador, em `http://localhost:4200`:

```js
await fetch('/api/auth/me');
const xsrf = document.cookie.split('; ').find(c => c.startsWith('XSRF-TOKEN=')).split('=')[1];
await (await fetch('/api/saloes/<salão>/profissionais/<profissional>/google/verificacao', { method: 'POST', headers: { 'X-XSRF-TOKEN': decodeURIComponent(xsrf) } })).json();
```

A resposta traz cada passo (criar, alterar, listar e apagar um evento na agenda dela) com `ok` e o erro do Google, quando houver. O evento da verificação é apagado no fim, mesmo quando um passo do meio falha.

## Salão piloto

O salão e a dona entram por um comando da própria API, com os dados fora do git. Com o compose no ar:

```powershell
docker compose exec api dotnet run --no-build --project ViAgendamentos.Api --no-launch-profile -- salao-piloto --SalaoPiloto:Nome="Salão da Vi" --SalaoPiloto:Slug=salao-da-vi --SalaoPiloto:DonaNome=Vi --SalaoPiloto:DonaEmail=dona@gmail.com
```

A dona ganha usuário no primeiro login com Google, que liga a profissional pelo e-mail.

## Testes

Rodam no Windows. Pré-requisitos: SDK do .NET 10 e o Docker Desktop aberto, porque os testes do banco sobem um Postgres 17 descartável pelo Testcontainers.

```powershell
dotnet test
```
