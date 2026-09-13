# Topaz — encurtador de URLs

Implementação mínima do desafio em `TOPAZ - Desafio DEV - .NET.pdf`: URL original, alias opcional, geração serializada, API REST e redirecionamento. Inclui uma página MVC e testes. Um projeto executável e um projeto de testes.

## Executar

Pré-requisito: SDK .NET 10.

```powershell
cd E:\Repos\topaz
dotnet run --project src/Topaz --no-launch-profile --urls http://localhost:5080
```

Abra http://localhost:5080. Para verificar e publicar:

```powershell
dotnet test -c Release
dotnet publish src/Topaz -c Release -o artifacts/publish
```

## API

| Operação | Resultado |
| --- | --- |
| `POST /api/links` com `{"url":"https://example.com","alias":"exemplo"}` | 201, `Location: /r/exemplo`, corpo `{"code":"exemplo","shortUrl":"/r/exemplo"}` |
| Mesmo POST sem alias ou com alias vazio | Código aleatório de sete caracteres |
| URL ou alias inválido | 400 |
| Alias já reservado | 409 |
| `GET /r/exemplo` | 302 para a URL original |
| Código inexistente | 404 |

`shortUrl` é um caminho relativo à origem. O navegador acrescenta a origem atual para exibir o endereço completo; outros consumidores devem resolver o caminho contra a origem da API. Assim, o servidor não constrói URLs absolutas confiando no cabeçalho Host. `Url.RouteUrl` inclui o PathBase quando hospedado como aplicação no IIS.

Regras: HTTP/HTTPS, até 2048 caracteres, sem credenciais embutidas. Alias de 1 a 32 caracteres ASCII alfanuméricos, hífen ou sublinhado. Espaços nas extremidades são removidos. Maiúsculas e minúsculas diferenciam aliases. A mesma URL pode gerar vários links. O prefixo `/r/` evita colisão com rotas da API e arquivos estáticos. O serviço não acessa nem verifica a existência do destino.

## Tecnologias e motivos

- **C# / .NET 10 / ASP.NET Core:** SDK instalado, versão LTS e hospedagem compatível com IIS. A aplicação usa apenas o framework compartilhado, sem pacotes de produção adicionais. [Política oficial de suporte](https://dotnet.microsoft.com/en-us/platform/support/policy).
- **MVC com Razor:** atende à opção de interface MVC do desafio, serve HTML e API no mesmo processo e dispensa build separado de frontend. JavaScript nativo usa `fetch`; CSS simples é suficiente para o escopo. Angular acrescentaria dependências e configuração sem benefício proporcional neste formulário.
- **Memória com ConcurrentDictionary:** persistência é opcional. Busca e reserva são simples; `TryAdd` não sobrescreve um alias. Permite leituras seguras enquanto a geração escreve. Não há motivo para adicionar EF ou Dapper sem banco.
- **SemaphoreSlim(1, 1):** somente uma criação entra no motor por vez. `WaitAsync` permite aguardar sem prender uma thread; o token cancela a espera caso o cliente desista. `finally` libera o bloqueio inclusive em erros. O serviço é singleton para todas as requisições compartilharem o mesmo semáforo. As leituras não entram nessa fila.
- **RandomNumberGenerator.GetString:** gera sete caracteres a partir de 62 símbolos, sem biblioteca externa; são 62^7 combinações possíveis. Aleatoriedade não garante unicidade: a reserva por `TryAdd` detecta colisões e repete até dez vezes. Esgotamento retorna 409 para manter a resposta simples e limitada.
- **xUnit:** testes de regras, resolução, concorrência e recuperação após exceção. Pacotes de testes vêm do template do SDK; não há infraestrutura de testes em produção.

## Organização, SOLID e Clean Code

```text
src/Topaz/
  Program.cs                      composição e registros de dependência
  Controllers/LinksController.cs  HTTP e apresentação
  Links/LinkService.cs             regras e serialização
  Links/ILinkStore.cs              contrato mínimo de armazenamento
  Links/InMemoryLinkStore.cs       armazenamento concorrente
  Views/Links/Index.cshtml          formulário
  wwwroot/                        JavaScript e CSS
tests/Topaz.Tests/                 testes do serviço
```

**S — responsabilidade única:** controller traduz HTTP, serviço executa o caso de uso, store armazena. A view apresenta dados. Validação e geração permanecem juntas porque compõem um único caso de uso pequeno.

**O — aberto para extensão:** outra implementação de `ILinkStore` pode substituir a memória pelo registro no contêiner, sem alterar as regras. Um banco com I/O assíncrono exigiria evoluir o contrato para métodos assíncronos; não escondemos essa mudança futura.

**L — substituição:** qualquer store deve preservar o contrato: `TryAdd` reserva atomicamente, retorna false sem sobrescrever e `Find` retorna null para ausência. Uma implementação que sobrescreva aliases violaria esse contrato.

**I — interfaces pequenas:** o store expõe somente os dois métodos necessários. Não existe repositório genérico com CRUD sem uso.

**D — inversão de dependência:** `LinkService` recebe `ILinkStore` no construtor. A implementação é escolhida no `Program.cs`. Não criamos interface para cada classe; o controller usa o serviço concreto porque não existe necessidade atual de substituí-lo.

Clean Code aparece em nomes que expressam intenção, retornos antecipados na validação, estados explícitos em `CreationResult` e ausência de exceções para erros esperados. A estrutura não exige projetos separados de Domain/Application/Infrastructure, CQRS, MediatR, AutoMapper ou abstrações adicionais.

## Métodos principais

| Método | Responsabilidade e escolha |
| --- | --- |
| `CreateAsync` | Aguarda o semáforo, valida, reserva alias ou código e devolve resultado independente de HTTP. O async é necessário pela espera do bloqueio. |
| `TryAdd` | Reserva o código atomicamente; false representa conflito esperado. |
| `Find` | Consulta sem alterar o estado; null representa ausência. |
| `Create` | Converte o resultado de negócio em 201, 400 ou 409. |
| `Resolve` | Consulta e devolve 302 ou 404; 302 evita marcar o redirecionamento como permanente. |
| `Index` | Entrega a view Razor. |
| `Dispose` | Libera o semáforo no encerramento; o contêiner gerencia o singleton. |

No frontend, `preventDefault` permite consumir a API sem recarregar a página. O botão é desabilitado durante o envio e reabilitado em `finally`. `textContent` exibe mensagens e URLs como texto, sem interpretar HTML.

## IIS

1. Habilite IIS e instale o **ASP.NET Core Hosting Bundle do .NET 10**. Se o IIS foi instalado depois do bundle, repare o bundle.
2. Publique com o comando acima. O Web SDK gera `web.config` em `artifacts/publish`.
3. Configure um site/aplicação no IIS apontando para a pasta publicada, com permissões de leitura/execução para sua identidade.
4. Use pool com **No Managed Code** e **um único worker process**. Configure binding e HTTPS conforme o ambiente.
5. Verifique o formulário, o POST e um redirecionamento no endereço do IIS.

[Documentação oficial do IIS](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/?view=aspnetcore-10.0).

## Limites e evolução

Validação local realizada: 9 testes aprovados em Release; chamadas HTTP verificadas para 201, 302 com destino original, 409, 400 e 404; formulário exercitado no Chromium com geração automática e alias; após correção do pattern HTML, geração automática confirmada sem erros de console. `dotnet publish` concluído com `web.config` gerado. Execução em IIS e teste específico no Edge não realizados.

Os links desaparecem ao reiniciar ou reciclar o pool. A memória cresce com cada link, sem expiração. O semáforo sincroniza somente dentro de um processo; web garden, múltiplas instâncias e múltiplos servidores não são suportados nesta versão. Não há garantia de ordem FIFO na espera.

Com mais tempo e necessidade real: armazenamento durável com índice único, contrato de I/O assíncrono, fila/coordenação global para geração entre instâncias, expiração, limite de requisições e proteção contra abuso. Acrescentaria testes de integração e pipeline após definir o ambiente de entrega. Docker e deploy em nuvem são opcionais e foram omitidos para manter o mínimo solicitado.

O repositório Git é local; publicação em GitHub/GitLab depende de um destino. Publicação por `dotnet publish` comprova a geração dos artefatos, não a execução em IIS. A validação de IIS precisa ocorrer no servidor configurado.
