# XoMacro

Aplicativo Windows para gravar e repetir sequências de teclado e mouse. Você define os passos de um macro, escolhe um **Trigger** (tecla ou botão extra do mouse) e segura esse Trigger na janela do programa alvo para reproduzir.

Versão atual: **v65**. O título da janela é só `XoMacro`; a versão aparece no chip ao lado do nome.

## O que ele faz

- Lista de macros à esquerda, passos à direita
- Passos do tipo combinação de teclas, clique, texto ou espera
- Trigger por tecla ou mouse (meio, lateral 4, lateral 5, direito)
- Restringe o Trigger a um programa específico (combo **Programa**)
- Repete enquanto o Trigger está pressionado, ou continua de onde parou (**Por etapa**)
- Ciclos: um passo pode tocar só em algumas voltas da sequência
- Velocidade da reprodução e loop infinito
- Marca d’água arrastável (verde = reproduzindo, vermelho = parado), inclusive em outro monitor
- Tema claro e escuro
- Minimizar para a bandeja (o Trigger continua ligado)

## Como usar

1. Abra o `XoMacro.exe` (não use o `DailyMacro.exe` antigo).
2. Crie ou selecione um macro e marque **Macro ativo**.
3. Em **Programa**, escolha a janela alvo (ou deixe “Qualquer programa”).
4. Defina o Trigger: combo do mouse e/ou botão **Tecla**.
5. Adicione passos com **+ Passo**.
6. Foque a janela do jogo/app e **segure** o Trigger para reproduzir. Solte para parar.

Importar e exportar usam JSON de um único macro. O checkbox na lista liga ou desliga o Trigger daquele macro.

## Onde os dados ficam

Pasta principal:

`%AppData%\XoMacro`

| Arquivo | Conteúdo |
| --- | --- |
| `macros.json` | Todos os macros e passos |
| `settings.json` | Tema, velocidade, marca d’água, loop |
| `crash.log` | Último erro se o app não abrir |

Caminho típico:

`C:\Users\SEU_USUARIO\AppData\Roaming\XoMacro\macros.json`

Na primeira abertura, se essa pasta ainda não tiver arquivos, o app copia o que existir em `%AppData%\DailyMacro`. Ao carregar, ele também escolhe a lista **mais completa** entre:

- `%AppData%\XoMacro\macros.json`
- `%AppData%\DailyMacro\macros.json`
- `%UserProfile%\Pictures\macros.json`

O `DailyMacro.exe` (v58 e anteriores) **só lê** a pasta `DailyMacro`. Se a lista vier vazia, você provavelmente abriu o exe antigo.

## Requisitos

- Windows 10/11
- Permissão de administrador (hooks globais de teclado e mouse)
- Para gerar o exe a partir do código: [.NET 8 SDK](https://dotnet.microsoft.com/download)

## Publicar o exe

No diretório do projeto:

```powershell
dotnet publish DailyMacro.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ..\dist-v65
```

O resultado é um único `XoMacro.exe` (hoje em `dist-v65`). Antes de publicar, suba `AppInfo.Build` e `<Version>` no `.csproj` para o número da pasta bater com o chip da UI.

## Estrutura do código

```
DailyMacro/
  Program.cs              entrada
  AppInfo.cs              nome e versão
  MainForm.cs             janela principal
  Models/                 macros, passos, teclas
  Services/
    MacroStore.cs         JSON em AppData
    HotkeyService.cs      Trigger (hooks em thread própria)
    MacroPlayer.cs        reprodução
    InputSender.cs        envio de teclado/mouse
    ProcessHelper.cs      janelas e processos
  UI/                     tema, diálogos, marca d’água
```

O namespace interno ainda se chama `DailyMacro`; o produto e o exe são **XoMacro**.
