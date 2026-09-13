# Encurtador de URLs — Topaz

Projeto desenvolvido para o desafio técnico da Topaz. A aplicação recebe uma URL e gera um link curto para redirecionar ao endereço original. Também permite escolher um alias, desde que ele ainda esteja disponível.

Mantive a solução em um projeto web e um projeto de testes. Como o problema é pequeno, a ideia foi deixar o fluxo fácil de acompanhar e a aplicação simples de executar.

## Como rodar

É necessário ter o SDK do .NET 10 instalado. Na pasta do repositório, execute:

```powershell
dotnet run --project src/Topaz --no-launch-profile --urls http://localhost:5080
```

Acesse [http://localhost:5080](http://localhost:5080), informe a URL e clique em **Gerar link**. O alias é opcional. Depois de gerar, basta copiar o endereço exibido.

Para executar os testes:

```powershell
dotnet test -c Release
```

## Escolhas técnicas

Usei **C# com .NET 10 e ASP.NET Core MVC**. O .NET 10 é uma versão LTS e permite hospedar a aplicação no IIS, como o desafio pede. O MVC reúne a API e a página Razor no mesmo projeto. Para um formulário com dois campos, JavaScript nativo e CSS atendem bem, sem precisar de uma aplicação Angular separada.

Como a persistência é opcional, os links ficam em memória, em um `ConcurrentDictionary`. Isso permite consultar e cadastrar links com segurança entre threads, sem configurar um banco. A consequência é que os dados são perdidos quando a aplicação reinicia, inclusive após a reciclagem do pool no IIS.

A aplicação não usa pacotes externos em produção. Nos testes, usei xUnit.

### Uma geração por vez

Esse é um requisito específico do desafio. Usei `SemaphoreSlim(1, 1)` no `LinkService` para que apenas uma requisição por vez execute a validação e a criação do link.

O serviço foi registrado como singleton: todas as requisições compartilham a mesma instância e o mesmo semáforo. `WaitAsync` permite aguardar a vez sem bloquear uma thread, e o cancelamento da requisição pode interromper essa espera. O `finally` libera o semáforo mesmo se ocorrer uma exceção.

A consulta de um link existente não precisa passar por essa fila. Esse controle vale para um único processo; para executar várias instâncias, seria necessário coordenar a geração entre elas.

### Geração dos códigos

Quando o usuário não informa um alias, `RandomNumberGenerator.GetString` gera um código de sete caracteres, usando letras e números. São 62 símbolos disponíveis em cada posição.

Ainda pode haver uma colisão. Por isso, o código só é aceito quando `TryAdd` consegue reservá-lo. Se já existir, a aplicação tenta outro, com limite de dez tentativas. Caso todas falhem, retorna 409 e pede uma nova tentativa.

O alias informado pelo usuário passa pela mesma reserva. Se estiver ocupado, retorna conflito sem alterar o link já cadastrado.

## Organização do código

```text
src/Topaz/
  Program.cs                      configuração da aplicação e dependências
  Controllers/LinksController.cs  rotas e respostas HTTP
  Links/LinkService.cs             validação e criação dos links
  Links/ILinkStore.cs              contrato de armazenamento
  Links/InMemoryLinkStore.cs       implementação em memória
  Views/Links/Index.cshtml          página do formulário
  wwwroot/                        JavaScript e CSS
tests/Topaz.Tests/                 testes do serviço
```

A separação segue as responsabilidades de cada parte: o controller recebe a requisição, o serviço aplica as regras e o armazenamento guarda os links. O serviço devolve um `CreationResult`, e o controller transforma esse resultado em uma resposta HTTP.

Apliquei SOLID onde ajuda a manter essa estrutura simples. O `LinkService` depende de `ILinkStore`, que tem apenas duas operações: reservar e consultar um código. Isso permite substituir o armazenamento sem misturar a implementação com as regras de geração.

Uma implementação desse contrato deve manter o mesmo comportamento: reservar sem sobrescrever e retornar `null` quando não encontrar um código. Para adotar um banco com acesso assíncrono, também seria necessário ajustar os métodos da interface.

Mantive nomes diretos e retornos antecipados nas validações. URL inválida e alias duplicado são resultados esperados, então não são tratados como exceções. Não separei a aplicação em vários projetos de camadas porque, neste tamanho, as pastas e a interface de armazenamento já deixam as responsabilidades claras.

### Principais métodos

| Método | O que faz |
| --- | --- |
| `CreateAsync` | Aguarda o semáforo, valida os dados e reserva o alias ou um código gerado. |
| `TryAdd` | Tenta reservar o código de forma atômica. Retorna `false` se ele já existir. |
| `Find` | Busca a URL original; retorna `null` quando não encontra. |
| `Create` | Recebe o POST e converte o resultado do serviço em 201, 400 ou 409. |
| `Resolve` | Redireciona para a URL original ou retorna 404. |
| `Index` | Entrega a página Razor. |
| `Dispose` | Descarta o semáforo no encerramento do serviço. |

Na página, o JavaScript envia os dados com `fetch` e exibe o resultado sem recarregar. O botão fica desabilitado durante o envio. Mensagens e URLs são inseridas com `textContent`, sem interpretar o conteúdo como HTML.

## API

Para criar um link:

```http
POST /api/links
Content-Type: application/json

{
  "url": "https://example.com/pagina",
  "alias": "exemplo"
}
```

Resposta: **201 Created**, com `Location: /r/exemplo` e o corpo:

```json
{
  "code": "exemplo",
  "shortUrl": "/r/exemplo"
}
```

Para gerar um código automático, basta omitir `alias` ou deixá-lo vazio.

`shortUrl` é um caminho relativo à origem. A página acrescenta o domínio atual para mostrar o link completo. Outros consumidores da API devem fazer o mesmo. Assim, o servidor não precisa montar um endereço absoluto a partir do cabeçalho `Host`. A rota é gerada com `Url.RouteUrl`, que considera o caminho base da aplicação.

| Situação | Resposta |
| --- | --- |
| Link criado | 201 Created |
| URL ou alias inválido | 400 Bad Request |
| Alias já utilizado | 409 Conflict |
| Acesso a `/r/{code}` existente | 302 Found, com redirecionamento |
| Código não encontrado | 404 Not Found |

Usei 302 para não marcar o redirecionamento como permanente. O prefixo `/r/` separa os links das rotas da API e dos arquivos estáticos.

### Validações

- A URL precisa usar HTTP ou HTTPS, ter até 2048 caracteres e não conter credenciais embutidas.
- O alias aceita de 1 a 32 caracteres: letras sem acentos, números, hífen e sublinhado.
- Espaços no começo e no fim são removidos.
- Maiúsculas e minúsculas diferenciam aliases: `Teste` e `teste` são distintos.

A mesma URL pode ter vários links curtos. A aplicação valida o formato do endereço, mas não acessa o destino para verificar se ele existe.

## Publicação no IIS

Gere os arquivos de publicação:

```powershell
dotnet publish src/Topaz -c Release -o artifacts/publish
```

No servidor:

1. Habilite o IIS e instale o **ASP.NET Core Hosting Bundle do .NET 10**. Se o IIS foi instalado depois do bundle, repare a instalação do bundle.
2. Configure um site ou aplicação apontando para a pasta publicada, com permissão de leitura e execução para a identidade do pool.
3. Configure o pool como **No Managed Code**, com **um único worker process**.
4. Ajuste o binding e o HTTPS para o ambiente e confira a criação e o redirecionamento de um link.

O `web.config` é gerado pelo SDK durante a publicação.

## Testes e validação

Os nove testes automatizados cobrem geração, consulta, entradas inválidas, disputa pelo mesmo alias, execução de uma geração por vez e liberação do semáforo após uma falha.

Na validação local, os testes passaram em Release. Também foram conferidas as respostas HTTP 201, 302, 400, 409 e 404, além do formulário no Chromium. A geração automática foi verificada sem erros de console.

A publicação Release gerou os arquivos e o `web.config`. A execução no IIS e um teste específico no Edge ainda não foram realizados. O código está versionado no [GitHub](https://github.com/digomontilha/topaz).

## O que faria com mais tempo

A primeira mudança seria persistir os links em banco, com índice único no código e acesso assíncrono. Também adicionaria expiração e limite de requisições, já que a versão atual acumula os links em memória sem limite.

Se fosse necessário rodar mais de uma instância, a geração precisaria de uma fila ou de outra forma de coordenação compartilhada. O semáforo atual resolve a concorrência dentro do processo, mas não entre servidores, e não garante ordem de chegada.

Por fim, acrescentaria testes de integração e um pipeline para executar os testes e gerar a publicação. Docker ficou de fora desta entrega para manter o foco no fluxo pedido e na hospedagem em IIS.

## Referências

- [Política de suporte do .NET](https://dotnet.microsoft.com/en-us/platform/support/policy)
- [Hospedagem do ASP.NET Core no IIS](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/?view=aspnetcore-10.0)
