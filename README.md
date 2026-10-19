# Torrent Classic

Cliente BitTorrent **portátil, gratuito e open source para Windows**, inspirado na organização do µTorrent Classic atual. Interface nativa, sem anúncios, subscrições, telemetria ou navegador embutido. Temas claro, escuro e do sistema.

Implementação independente em C# / Windows Forms com o motor [MonoTorrent](https://github.com/alanmcgovern/monotorrent). Não utiliza código, executáveis, ícones ou logótipos do µTorrent e não tem afiliação com µTorrent, BitTorrent ou Rainberry.

## Descarregar e abrir

1. Descarrega o ZIP portátil na página de [Releases](https://github.com/pereira-andre/torrent-classic/releases).
2. Extrai **a pasta completa** para uma localização onde possas escrever, incluindo uma pen USB.
3. Abre `TorrentClassic.exe`. Não exige instalador, administrador nem instalação separada do .NET.

Windows 10/11 de 64 bits (x64). A distribuição inclui o runtime .NET; por isso o executável é maior do que o µTorrent original. O executável não usa compressão interna, para evitar descompressão de código em cada arranque. O ZIP reduz o tamanho do download.

Na primeira execução são criadas as pastas `Data/` (sessão, preferências e cache) e `Downloads/` junto ao executável. Fecha a aplicação antes de mover a pasta; os destinos dentro desta pasta são ajustados automaticamente na próxima execução. Destinos externos permanecem absolutos. Não são criadas associações de ficheiros nem entradas de arranque no registo. O runtime pode extrair bibliotecas nativas para a pasta temporária normal do Windows.

## O que está implementado

- Ficheiros `.torrent` e magnet links, incluindo metadados por peers.
- Transferência e partilha reais, DHT, PEX, descoberta local, trackers HTTP/HTTPS/UDP e mapeamento UPnP/NAT-PMP através do MonoTorrent.
- Iniciar, pausar, parar, verificar integridade e retomar a sessão após fechar.
- Seleção múltipla, arrastar ficheiros/magnets, pesquisa, ordenação, categorias e etiquetas.
- Separadores Geral, Trackers, Peers, Ficheiros, Velocidade e Registo com dados reais.
- Prioridades por ficheiro: alta, normal, baixa e não transferir.
- Limites globais de download/upload, porta e máximo de ligações configuráveis.
- Criação de torrents a partir de ficheiros ou pastas, com tracker opcional.
- Área de notificação e instância única, sem perder novos argumentos ao reabrir o executável.
- Remoção da lista mantendo os ficheiros no disco.

Esta primeira versão recria o fluxo principal e a disposição clássica, não toda a gama de funcionalidades do µTorrent. Ainda não inclui RSS, agendador, fila automática, controlo remoto, leitor multimédia ou antivírus. Não promete compatibilidade com Windows XP/7/8 nem igualdade de consumo de memória com o cliente original. O executável não está assinado digitalmente.

## Atalhos

| Ação | Atalho |
| --- | --- |
| Adicionar torrent | Ctrl+O |
| Adicionar magnet | Ctrl+U |
| Criar torrent | Ctrl+N |
| Preferências | Ctrl+P |
| Pesquisar | Ctrl+F |
| Selecionar todos | Ctrl+A |
| Remover da lista | Delete |
| Mostrar/ocultar detalhes | F5 |
| Mostrar/ocultar barra lateral | F7 |

Botão direito num torrent para abrir a pasta, copiar o magnet, definir etiqueta, verificar ou atualizar trackers. No separador Ficheiros, seleciona ficheiros e usa o botão direito para mudar prioridades. O rácio e os contadores de bytes transferidos apresentados são da sessão atual; a conclusão e o estado de execução persistem.

## Compilar e testar

Requer SDK .NET 10. A publicação para Windows também pode ser feita a partir de macOS/Linux; a interface só corre em Windows.

```sh
dotnet restore src/TorrentClassic.App
dotnet run --project tests/TorrentClassic.Tests -c Release
dotnet build src/TorrentClassic.App -c Release
```

No PowerShell, criar o pacote portátil completo com licenças e SHA256:

```powershell
./scripts/publish.ps1
```

Para compilar apenas o executável a partir de outro sistema:

```sh
dotnet publish src/TorrentClassic.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=false -o artifacts/win-x64
```

Os testes de integração criam um torrent de dados sintéticos e transferem-no exclusivamente em loopback entre dois motores: verificam metadados magnet, pausa, persistência, retoma, prioridades, SHA256, duplicados, remoção sem apagar dados e mudança da pasta portátil. A CI executa-os em Windows e macOS, compila o `.exe` e abre as janelas em Windows para gerar capturas dos dois temas.

## Licenças e inspiração

O código e os ícones originais desta aplicação são distribuídos sob a [licença MIT](LICENSE). As distribuições do projeto são gratuitas. A licença MIT permite a reutilização por terceiros segundo os seus próprios termos.

As bibliotecas MonoTorrent 3.0.2, Mono.Nat 3.0.0 e ReusableTasks 4.0.0 são MIT, com os avisos de copyright e textos integrais preservados em [licenses/](licenses/). O runtime .NET e os seus componentes mantêm as respetivas licenças e avisos; a publicação inclui os textos fornecidos nos pacotes Microsoft. Consulta [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

Referência visual: [µTorrent Classic](https://www.utorrent.com/desktop/). µTorrent é uma marca dos seus titulares. A inspiração visual não concede direitos sobre essa marca ou sobre o software proprietário. O [REA](https://github.com/morluto/rea) foi considerado como ferramenta de análise; esta implementação não inclui REA nem resulta da descompilação do cliente original.
