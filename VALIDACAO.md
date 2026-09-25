# Efesto — validação de 25/09/2026

## Compatibilidade com o script legado

O erro do `sigadmweb` era `ASPCONFIG: Could not load file or assembly 'Foxit.PDF.Rasterizer.40.x64' ... An attempt was made to load a program with an incorrect format`.

A comparação local mostrou:

| Inicialização do VsDevCmd.bat | aspnet_compiler escolhido pelo PATH |
|---|---|
| Sem argumentos de arquitetura, na primeira versão WinUI | `C:\Windows\Microsoft.NET\Framework\v4.0.30319\aspnet_compiler.exe` (32 bits) |
| `-arch=arm -host_arch=amd64`, como no `ApplicationCompiler\assets\CMD.txt` original | `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\aspnet_compiler.exe` (64 bits) |

A chamada sem esses argumentos foi uma divergência da migração e foi corrigida. O modo ASP.NET agora usa exatamente os argumentos de ambiente do legado, mantendo `-nologo -p <fontes> -fixednames -f -v /<nome> <saída>`. O log registra o ambiente solicitado e o caminho encontrado por `where aspnet_compiler`.

O script legado não é executado literalmente: sua preparação/compilação/limpeza foi portada para o motor C#. As diferenças intencionais são aspas nos caminhos, validação do código de saída, preparação em diretório exclusivo, cancelamento e preservação da publicação anterior, em vez de apagá-la antes do build.

## Teste com a aplicação real

- Fontes: `C:\Superlogica.Git\ahreas-web-administracao\app`.
- Configuração: padrão do projeto, com transformação de `web.publish.config`.
- Resultado: **sucesso**, código de saída 0, em **00:09:43**.
- Publicação de verificação: `artifacts\qa\efesto-real-sigadmweb\sigadmweb`.
- Log completo: `artifacts\qa\efesto-real-sigadmweb\_logs`.
- O erro do Foxit não reapareceu. O compilador emitiu avisos `CS1685` sobre definições duplicadas e um aviso sobre referências de `Telerik.Windows.Documents.Spreadsheet.FormatProviders.Xls` / `System.IO.Compression`; eles não impediram a compilação. O teste confirma a pré-compilação, não uma validação funcional do site em IIS.
- O destino de publicação usado pelo usuário não foi alterado pelo teste.

## Demais verificações

- Build WinUI Release sem erros ou avisos.
- **42 testes automatizados passaram**, incluindo regressão dos argumentos do Dev CMD, configs completos/XDT, configurações anteriores, cancelamento, backup e os dois modos de ZIP.
- Dois sites de exemplo compilados pelo executor publicado, produzindo dois ZIPs individuais. Ambos contêm `web.config` e os demais arquivos diretamente na raiz.
- Inspeção da interface em execução: nome/ícone Efesto, seletor de compactação, cabeçalho com nome/status e recolhimento de todo o bloco.
- Salvamento do modo de compactação e estado expandido/recolhido verificado no JSON.

Executável atualizado: `artifacts\Efesto\Efesto.exe`. As configurações anteriores continuam no mesmo local em `%LOCALAPPDATA%\ApplicationCompiler.WinUI\settings.json`.

## ZIP independente por aplicação — 25/09/2026

- **49 testes passaram**. Cobertura adicionada para seleção individual com/sem ZIP geral, conteúdo dos arquivos, falha parcial e migração do antigo modo global individual.
- Interface e executor publicados novamente por `build.ps1`.
- Controle **Gerar ZIP desta aplicação** verificado no cabeçalho, com bloco aberto e recolhido. Marcar a opção não recolhe o bloco.
- Teste pela interface com duas aplicações de exemplo: ZIP geral marcado, Demo marcada individualmente, Second desmarcada. Resultado: dois builds bem-sucedidos e exatamente dois ZIPs, um geral contendo Demo/Second e outro apenas de Demo com arquivos na raiz.
- Preferências independentes e estado recolhido conferidos no JSON salvo automaticamente.
- Artefatos isolados em `artifacts\qa\zip-selection-20260925-091328`; fontes e destino de produção não usados neste teste.

## Seleção de aplicações por processamento — 25/09/2026

- **51 testes passaram**, incluindo configuração antiga selecionada por padrão, persistência das escolhas, nenhuma seleção, aplicação desmarcada com fontes/config indisponíveis e preservação da publicação anterior fora dos ZIPs.
- Interface e executor publicados em Release. Verificação visual com duas aplicações recolhidas: check de compilação e de ZIP independentes, contador de selecionadas e aviso ao tentar executar sem seleção.
- Pela interface, apenas a segunda aplicação (Demo) foi selecionada e executada. A primeira (Offline), com origem inexistente e ZIP marcado, permaneceu sem execução. O ZIP geral incluiu somente Demo e o individual teve os arquivos na raiz.
- Executor publicado também executou somente Demo com o mesmo resultado. Seleção salva conferida no JSON.
- Testes isolados em `artifacts\qa\application-selection`, sem usar a saída de produção.

## Distribuição mínima de Release — 25/09/2026

- `build.ps1` agora cria automaticamente `artifacts\Efesto-minimal` após publicar a interface e o executor.
- A distribuição mantém o runtime self-contained do .NET/Windows App SDK, XAML/PRI, `Assets`, recursos `pt-BR`, e `runner`; remove símbolos, documentação XML e satélites de idiomas não utilizados.
- O executável mínimo foi iniciado diretamente de `artifacts\Efesto-minimal\Efesto.exe` e permaneceu responsivo. A publicação completa não foi alterada.
- Resultado desta publicação: **563 arquivos, 302,2 MB**. O WinUI self-contained exige os componentes nativos; portanto, a versão mínima não pode ser reduzida ao `.exe` e uma DLL como no compilador .NET Framework antigo.

## Release e instalador — 25/09/2026

- `Version.props` passou a ser a fonte interna da versão, iniciando em **1.0.0**. `build.ps1` aceita `-Version major.minor.patch`, incrementa automaticamente o patch após uma Release bem-sucedida e aceita `-NoVersionIncrement`.
- `installer\Efesto.iss` foi criado para Inno Setup 6. O instalador é por usuário, instala em `%LOCALAPPDATA%\Programs\Efesto`, cria atalhos e preserva a configuração do usuário ao desinstalar.
- `build.ps1` procura `ISCC.exe` e gera `artifacts\installer\Efesto-Setup-<versão>.exe` quando o Inno Setup está instalado. Após corrigir a compatibilidade com o Inno Setup 6.2.1 (`x64`), o instalador **1.0.0 foi gerado com sucesso**, com 83,0 MB.
- A assinatura deve ser aplicada com certificado de código e `signtool`, usando SHA-256 e carimbo de tempo; os comandos estão documentados no README.

## Renomeação e publicação do repositório — 25/09/2026

- A solução agora é `Efesto.sln`; o projeto WinUI está em `src\Efesto\Efesto.csproj`, com namespace `Efesto`, manifesto e perfil do Visual Studio atualizados.
- `build.ps1`, o instalador e o gerador de logo usam o novo caminho. `dotnet sln Efesto.sln list` confirmou os quatro projetos esperados.
- Os 51 testes passaram após a renomeação e o projeto WinUI compilou em Release sem erros.
- `Publish-ToGitHub.ps1` foi criado para inicializar o Git, configurar `origin` para `https://github.com/RafaelSouzaValerio/Efesto.git`, criar a branch `main`, fazer o commit e enviar o projeto. O script não armazena credenciais.
- O caminho `%LOCALAPPDATA%\ApplicationCompiler.WinUI` foi mantido intencionalmente para migrar as configurações já existentes.

## Renomeação completa dos projetos e pastas — 25/09/2026

- Pasta principal: `C:\Callistto\Efesto`. Solução: `Efesto.sln`.
- Projetos, pastas, arquivos `.csproj` e namespaces: `Efesto`, `Efesto.Core`, `Efesto.Runner` e `Efesto.Tests`.
- Referências entre projetos, scripts de build/distribuição e exportação de BAT atualizados para `Efesto.Runner.exe`.
- 51 testes aprovados. Publicação Release feita a partir da nova pasta; distribuição completa e mínima sem binários `ApplicationCompiler*`.
- O executor publicado compilou o exemplo e criou ZIP geral e individual em `artifacts\qa\renamed-projects\output`.
- Instalador `artifacts\installer\Efesto-Setup-1.0.1.exe` gerado com sucesso pelo Inno Setup 6.2.1, usando a versão já configurada no projeto, sem incremento durante a validação.
- Artefatos anteriores preservados em `artifacts\rename-backup-20260925-135529` para recuperação. Configurações salvas dos usuários continuam no caminho compatível anterior.

## Etapas e tempo de compilação — 25/09/2026

- O motor informa etapas estruturadas à interface; o status não depende de interpretar mensagens do compilador.
- Cada aplicação mostra o tempo desde a saída da fila até a finalização, atualizado pelo timer da interface mesmo sem novas linhas de log. Cancelamento preserva o contador e o tempo final inclui a limpeza dos temporários.
- 51 testes passaram, incluindo verificações das etapas durante execução do BAT e cancelamento, além da preservação dos fontes no fluxo ASP.NET com transformação.
- Conferência visual em Release, usando configuração isolada em `artifacts/qa/elapsed-20260925-140632`: duas aplicações mostraram `Executando BAT · 00:00:13` e a terceira `Na fila`; a primeira foi cancelada em 23 segundos, a segunda concluiu em 30 segundos e a terceira iniciou seu próprio contador ao sair da fila.
- A cópia dos fontes foi mantida após comparação com `C:\Callistto\ApplicationCompiler\frmCompilar.cs`: a compilação pelo aplicativo legado também copiava os fontes, transformava `web.config` e removia os outros `.config` da raiz temporária antes de executar o compilador.

## Limpeza dos fontes temporários — 25/09/2026

- A finalização aguarda até cinco tentativas de exclusão, inclusive depois de cancelamento. O resultado só é devolvido após essa etapa e inclui eventuais avisos de limpeza.
- Arquivos somente leitura da cópia são removidos sem alterar os atributos dos originais. Bloqueios persistentes geram aviso no cartão e no resumo, com o caminho completo no log.
- Um registro exclusivo `.compiler-<GUID>.cleanup` permite retomar limpezas pendentes no próximo processamento no mesmo destino; registros inválidos, pastas sem registro e registros em uso são preservados.
- 55 testes cobrem os fluxos anteriores e os novos casos de arquivos somente leitura, bloqueio transitório após cancelamento, bloqueio persistente com recuperação posterior, concorrência de limpeza e falha ao criar o registro de recuperação. Encerramento forçado antes do registro de limpeza não possui garantia de recuperação automática.
