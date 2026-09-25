# Efesto · Compilador de aplicações

**Do código à publicação.** A identidade usa um monograma E com uma chama e base de bigorna, em âmbar e grafite. O logo vetorial, a prévia PNG e o ícone multirresolução ficam em `src\Efesto\Assets`. Para regenerar PNG/ICO a partir do SVG, execute `powershell.exe -NoProfile -STA -File tools\Export-Brand.ps1`.

Versão desktop em C# / .NET 10 / WinUI 3 baseada no fluxo de compilação do projeto `C:\Callistto\ApplicationCompiler` (WinForms / .NET Framework 4.8).

## Executar

Abra `artifacts\Efesto\Efesto.exe`. A publicação é Windows x64, sem MSIX, com os runtimes do .NET e Windows App SDK incluídos. Ao copiar para outra máquina, copie **toda a pasta** `artifacts\Efesto`, incluindo `runner` e `Assets`.

Cada Release também cria `artifacts\Efesto-minimal`, uma distribuição enxuta pronta para copiar. Ela mantém os arquivos de runtime e recursos necessários para o WinUI, o logo, os recursos em português e `runner` (usado pelos BATs exportados), removendo símbolos, documentação e satélites de idioma não utilizados. O WinUI self-contained não pode ser reduzido a apenas um `.exe`: os componentes nativos do Windows App SDK precisam acompanhar a aplicação. O executável dessa distribuição é `artifacts\Efesto-minimal\Efesto.exe`.

Para desenvolver, abra `Efesto.sln` no Visual Studio, selecione o projeto `Efesto` como inicial e a plataforma **x64**. Requer SDK .NET 10 e as ferramentas WinUI do Visual Studio. A pasta local do projeto foi renomeada para `C:\Callistto\Efesto`; reabra a solução nesse caminho se o Visual Studio ainda apontar para o diretório anterior.

```powershell
.\build.ps1
```

O script executa os testes, publica a interface e o executor, cria a distribuição mínima e, se o Inno Setup 6 estiver instalado, gera o instalador em `artifacts\installer`. Para abrir a interface pelo Visual Studio sem publicar, use o perfil `Efesto`. O botão **Gerar BAT** requer a publicação completa feita por `build.ps1`.

O **Rebuild** do Visual Studio recompila apenas `bin\Release`; ele não atualiza `artifacts`. Para gerar uma Release distribuível, execute `build.ps1`. O controle de versão fica em `Version.props`, começando em `1.0.0`. Cada Release bem-sucedido incrementa automaticamente o patch para a próxima compilação. É possível informar uma versão para uma Release específica (`.\build.ps1 -Version 2.0.0`) ou impedir o incremento (`-NoVersionIncrement`).

O arquivo `installer\Efesto.iss` usa o Inno Setup para criar um único `Efesto-Setup-<versão>.exe`, com instalação por usuário em `%LOCALAPPDATA%\Programs\Efesto`, atalhos e desinstalador. Instale o Inno Setup 6 para habilitar essa etapa; `-SkipInstaller` publica apenas os artefatos do aplicativo.

Para criar o primeiro commit e enviar o projeto ao repositório GitHub configurado, execute `.Publish-ToGitHub.ps1`. O script inicializa o Git se necessário, configura `origin`, cria ou reutiliza a branch `main`, faz o commit dos arquivos rastreáveis e executa o push. Ele não armazena credenciais; use o Git Credential Manager ou autenticação SSH já configurada. Use `-SkipPush` para revisar o commit local antes de enviar.

### Assinatura do instalador

Para distribuição externa, adquira um certificado de assinatura de código OV ou EV de uma autoridade certificadora. Para uso apenas interno, um certificado próprio funciona desde que a raiz seja instalada como confiável em todas as máquinas. Nunca versione o `.pfx` nem a senha no repositório.

Depois de instalar o Windows SDK, assine o instalador com `signtool` e um servidor de carimbo de tempo:

```powershell
signtool sign /fd SHA256 /f C:\segredos\efesto.pfx /p $env:EFESTO_CERT_PASSWORD /tr http://timestamp.digicert.com /td SHA256 artifacts\installer\Efesto-Setup-1.0.0.exe
signtool verify /pa /all /v artifacts\installer\Efesto-Setup-1.0.0.exe
```

Em um pipeline, mantenha o certificado em um cofre de segredos ou use um provedor de assinatura remota. O carimbo de tempo mantém a assinatura válida depois que o certificado expirar. Para uma distribuição confiável, assine também os executáveis e DLLs antes de criar o instalador, e assine o `Setup.exe` por último.

## Uso

1. Sem configuração salva, o aplicativo localiza o `VsDevCmd.bat` automaticamente com `vswhere`, verificando instalações válidas, e usa o ambiente e as pastas padrão como alternativa. Instalações sem esse arquivo, como SSMS, são ignoradas. Uma configuração salva tem prioridade, inclusive um campo deixado vazio para BATs independentes. Use **Detectar novamente** para buscar manualmente. Se não encontrar, use **Procurar**. O campo é opcional se todas as linhas forem BAT personalizado e não precisarem desse ambiente.
2. Selecione a pasta de saída.
3. Informe o nome e a pasta dos fontes da primeira aplicação.
4. Escolha **Site ASP.NET (.NET Framework)** ou **BAT personalizado**. O campo de BAT aparece apenas na segunda opção.
5. Se necessário, escolha o tratamento de **web.config** e informe o arquivo. Use **+ Adicionar compilação** para incluir outras aplicações.
6. Marque **Compilar esta aplicação** no cabeçalho dos blocos que deseja executar e clique em **Compilar**. As demais configurações ficam guardadas para outro processamento, sem validar seus caminhos, executar seus BATs ou incluir publicações antigas nos ZIPs. É necessário marcar pelo menos uma aplicação. Até duas compilações executam simultaneamente. Cada aplicação possui log e cancelamento individual; o botão principal cancela todas. Clique no cabeçalho da aplicação para expandir/recolher **o bloco inteiro**; as opções, o nome e o status permanecem visíveis. Uma falha expande o bloco automaticamente. O estado aberto/fechado também é salvo.

A seleção de aplicações é salva automaticamente e restaurada na próxima abertura. Aplicações novas e configurações antigas sem esse campo começam selecionadas. O BAT exportado também respeita a seleção salva.

O cabeçalho mostra a etapa atual e o tempo decorrido (`hh:mm:ss`), atualizado mesmo quando não há novas mensagens no log. Aplicações aguardando uma vaga mostram **Na fila**; o contador começa quando iniciam o processamento e inclui a preparação dos fontes, execução e limpeza dos temporários. O tempo final permanece visível após conclusão, falha ou cancelamento.

A cópia temporária dos fontes preserva o comportamento da compilação pelo aplicativo legado: o `web.config` é preparado antes da compilação e, em ASP.NET, os demais `.config` da raiz são removidos dessa cópia. A pasta de saída separada do compilador não substitui esse isolamento, pois a preparação modifica os arquivos usados como entrada. Os fontes originais são preservados e a pasta temporária é removida ao terminar.

A limpeza roda também em erro e cancelamento, com até cinco tentativas para arquivos bloqueados e remoção do atributo somente leitura apenas na cópia. O fechamento normal aguarda essa finalização. Se o Windows continuar impedindo a exclusão, a aplicação mostra **Limpeza pendente**, registra o caminho no log e, quando consegue gravar o registro de recuperação, tenta novamente no próximo processamento no mesmo destino. Cada registro só é criado após o encerramento do compilador; pastas sem esse registro e limpezas em andamento em outra instância não são removidas automaticamente.

Queda de energia ou encerramento forçado antes da etapa de limpeza pode deixar uma cópia sem registro de recuperação. Não é possível garantir exclusão nesses casos; essas pastas exigem conferência antes da remoção, pois o compilador filho pode continuar em execução.

Os campos, a lista de aplicações, os modos e caminhos de compilação e de web.config, e a opção ZIP são salvos automaticamente após uma alteração e ao fechar, em:

`%LOCALAPPDATA%\ApplicationCompiler.WinUI\settings.json`

Na próxima abertura, essa configuração é restaurada. Uma instalação sem configuração começa com uma única linha. A configuração antiga do WinForms não é importada automaticamente.

A mudança de nome para Efesto mantém o caminho de configuração da versão WinUI anterior, preservando os dados já salvos. Arquivos JSON anteriores sem os novos campos são carregados com o tratamento **Padrão do projeto**.

## web.config por compilação

| Modo | Comportamento |
|---|---|
| Padrão do projeto | Em ASP.NET, aplica o `web.publish.config` dos fontes se existir. Em BAT personalizado, não modifica o config. |
| Copiar arquivo completo | Copia o arquivo escolhido como `web.config`, substituindo o conteúdo completo. Não requer um `web.config` anterior. |
| Aplicar transformação (XDT) | Aplica o arquivo XDT escolhido ao `web.config` existente nos fontes. Requer o arquivo base e instruções `xdt:Transform`. |
| Manter web.config original | Mantém o config dos fontes, ignorando o `web.publish.config` automático. |

O tratamento ocorre **na cópia temporária, antes do compilador ou do BAT**, sem alterar os fontes nem o arquivo selecionado. Uma escolha explícita substitui o tratamento automático; não há duas transformações em sequência. O campo de arquivo aparece apenas em Copiar/Transformar. XML inválido, arquivo ausente e uma transformação escolhida por engano no modo Copiar são informados como erro antes da execução. O modo e o caminho também acompanham os BATs exportados.

As opções de compactação são independentes e podem ser usadas juntas:

- **Gerar ZIP com todas as aplicações selecionadas**, no ambiente de compilação: arquivo único com uma subpasta por aplicação selecionada, gerado somente quando todas as selecionadas têm sucesso.
- **Gerar ZIP desta aplicação**, no cabeçalho de cada bloco, visível mesmo recolhido: gera um ZIP somente para aquela aplicação, com os arquivos diretamente na raiz. Aplicações marcadas e bem-sucedidas podem ser compactadas mesmo se outra falhar. Um cancelamento global interrompe a compactação.

Cada escolha é salva e incluída na exportação de BAT. Configurações antigas com ZIP unificado continuam usando essa opção. O antigo modo global de ZIP por aplicação, quando habilitado, é convertido para a opção individual marcada em cada bloco.

## Fluxo ASP.NET

- Copia os fontes para uma pasta de trabalho exclusiva, ignorando `.git`, `.vs`, `node_modules` e `obj`.
- Prepara o `web.config` na cópia conforme o modo escolhido e remove outros `.config` da raiz dessa cópia, conforme o aplicativo original.
- Inicializa o Developer Command Prompt com os argumentos do `assets\CMD.txt` legado: `-arch=arm -host_arch=amd64`. Nesta instalação, isso seleciona o `aspnet_compiler.exe` de `C:\Windows\Microsoft.NET\Framework64\v4.0.30319`, necessário para carregar as bibliotecas Foxit x64. O argumento `arm` prepara o ambiente de ferramentas do Dev CMD; não converte o site ASP.NET em uma aplicação ARM.
- Registra os argumentos do Dev CMD e o resultado de `where aspnet_compiler` no log. Executa os mesmos parâmetros do script legado: `-nologo -p <fontes> -fixednames -f -v /<nome> <saída>`. Os caminhos são colocados entre aspas.
- Verifica o código de saída e os arquivos de `bin`.
- Remove da publicação as pastas auxiliares usadas pelo original: `App_Data`, `BuildProcessTemplates`, `Documentação`, `Documentacao`, `SP`, `SP_Instalacao`, `Stimul` e `Temp`; também remove `.sln`, páginas online/offline e arquivos de controle de atividades.
- Publica em `<saída>\<nome>`. Uma publicação anterior é movida para `<nome>.backup-<data>-<id>` e mantida para recuperação manual. A publicação anterior não é substituída em caso de falha ou cancelamento durante o build.
- Se habilitado, compacta conforme o modo selecionado, com nomes exclusivos; os diretórios de saída permanecem disponíveis.

O fluxo ASP.NET reproduz a preparação dos fontes, argumentos e limpeza do legado. A diferença intencional é publicar primeiro em uma pasta temporária, verificar o código de saída e preservar a publicação anterior como backup, em vez de apagá-la antes de compilar. O modo BAT personalizado executa o arquivo informado pelo usuário e mantém sua inicialização de Dev CMD sem os argumentos do fluxo ASP.NET.

O ambiente de compilação precisa disponibilizar `aspnet_compiler` e as dependências exigidas pelos sites originais. O exemplo em `examples\site` é um site mínimo para testar o fluxo, incluindo uma transformação XDT.

## Contrato do BAT personalizado

O BAT é executado após a inicialização opcional do Developer Command Prompt. O diretório de trabalho é uma **cópia temporária** dos fontes. O arquivo original do BAT é chamado sem alterações.

| Argumento | Variável | Conteúdo |
|---|---|---|
| `%~1` | `%AC_SOURCE%` | Cópia temporária dos fontes |
| `%~2` | `%AC_OUTPUT%` | Pasta de saída temporária, já criada |
| `%~3` | `%AC_NAME%` | Nome da aplicação |

Também existem os aliases `appPath`, `destinyPath` e `appName`. `destinyPath` representa a saída completa, **sem acrescentar o nome novamente**. Os placeholders textuais `{path_app}`, `{path_destiny}` e `{app_name}` do template antigo não são substituídos: adapte-os para as variáveis acima.

O BAT deve gravar os artefatos em `%AC_OUTPUT%` e terminar com `exit /b 0` no sucesso, ou um código diferente de zero na falha. Não use `pause`, `set /p` nem abra processos independentes: a execução não é interativa. A saída padrão e os erros aparecem no log. No modo personalizado não há limpeza automática; o config só é preparado se você selecionar explicitamente Copiar ou Transformar. O BAT deve incluir esse config preparado nos artefatos que produzir, se necessário.

Veja `examples\custom-build.bat`. Caminhos com espaços, acentos, `&` e parênteses são aceitos. Use aspas ao referenciar caminhos no seu BAT. Caminhos contendo `%`, aspas ou quebras de linha são rejeitados para evitar expansão pelo CMD. Junções e links simbólicos nos fontes/saída não são aceitos.

## Logs e exportação

Logs completos ficam em `<saída>\_logs`; a interface mantém o trecho mais recente para não crescer indefinidamente.

**Gerar BAT** salva um `.bat` e um `.json` com a configuração atual na pasta escolhida. Eles chamam `runner\Efesto.Runner.exe`, usando exatamente o mesmo motor de compilação. Mantenha o `.bat` e o `.json` juntos. O BAT referencia o caminho absoluto do executor publicado; gere novamente se mover a aplicação. O runner executa as linhas sequencialmente, suporta Ctrl+C e retorna `0` no sucesso, `1` na falha e `130` no cancelamento.

## Organização e escopo

- `src/Efesto`: interface nativa e persistência automática.
- `src/Efesto.Core`: validação, configuração, execução, transformação, backup e ZIP.
- `src/Efesto.Runner`: executor de console usado pelos BATs exportados.
- `tests/Efesto.Tests`: testes de integração do motor com processos CMD reais e cenários de falha/cancelamento.

Os nomes de projetos, arquivos `.csproj`, pastas e namespaces seguem essa mesma estrutura. O executor publicado é `runner\Efesto.Runner.exe`. BATs exportados anteriormente precisam ser gerados novamente para usar o novo caminho e nome do executor.

A migração cobre a tela de compilação que o `Program.cs` original abre. O formulário legado `frmAtualizar` (atualização de sites implantados) não faz parte deste fluxo.

Para inspeção ou testes com uma configuração separada, a variável de ambiente `APPLICATION_COMPILER_SETTINGS` pode indicar outro arquivo JSON. Ela só afeta a interface.
