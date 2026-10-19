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
- **RSS e Atom:** subscrições, atualização periódica com ETag, filtros de inclusão/exclusão, transferência manual ou automática e histórico para evitar duplicados.
- **Agendador semanal:** grelha de 7 × 24 horas, com modos normal, limitado, só partilha e pausa. As pausas manuais mantêm-se quando o horário muda.
- **Fila automática:** limites de torrents/downloads ativos, ordem ajustável, início forçado e objetivos de partilha por rácio ou duração.
- **Controlo remoto:** painel adaptado ao computador/telemóvel, chave de acesso, upload de `.torrent`, magnets, ações, prioridades, limites e consulta/transferência de itens RSS.
- Contadores de bytes, rácio e tempo de partilha persistentes entre sessões.

O projeto recria a disposição e os fluxos principais do µTorrent Classic com uma implementação independente. O painel remoto funciona na rede local ou através de VPN; não depende do serviço na nuvem µTorrent Remote. Não inclui leitor multimédia nem antivírus. Não promete compatibilidade com Windows XP/7/8 nem igualdade de consumo de memória com o cliente original. O executável não está assinado digitalmente.

## RSS

Seleciona **Feeds RSS → Adicionar feed**, indica o endereço RSS/Atom e escolhe o intervalo de atualização. Os itens são mostrados nessa vista; seleciona-os e usa **Transferir selecionados**. Para automatizar, ativa a respetiva opção e define os filtros. Um feed desativado deixa de ser atualizado automaticamente.

Os filtros não distinguem maiúsculas de minúsculas: `*` corresponde a qualquer sequência, `?` a um carácter e `|` separa alternativas. Por exemplo, incluir `Ubuntu*|Debian*` e excluir `*beta*|*arm*`. Incluir vazio aceita todos os títulos. A opção automática também considera os itens que já estão no feed na primeira atualização. Erros num item não bloqueiam os restantes. O histórico guarda os últimos 2000 identificadores por feed; a lista mostra até 500 itens.

## Fila e agendador

Em **Preferências → Fila**, configura o máximo de torrents ativos e de downloads ativos. Os torrents em espera mostram **Em fila** e iniciam quando existe uma vaga. As setas da barra alteram a ordem. Os objetivos de partilha param o torrent quando atinge o rácio **ou** o tempo configurado; 0 desativa esse objetivo. O rácio usa os bytes enviados divididos pelo maior valor entre o tamanho do torrent e os bytes recebidos, para também funcionar com ficheiros que já existiam no disco.

**Início forçado** ignora a fila e os objetivos de partilha, mas respeita o agendador. Torrents forçados não ocupam vagas da fila normal. Pausar ou parar manualmente um torrent impede que a automação o reinicie.

Em **Preferências → Agendador**, escolhe o modo e pinta as horas da semana com o rato. O botão direito alterna o modo; também podes usar as setas e Espaço. O modo limitado aplica o menor limite não nulo entre o limite global e o do agendador. O modo só partilha permite torrents completos; para partilhar dados existentes ainda não verificados, faz primeiro **Forçar verificação**. O horário é o do Windows, incluindo a mudança de hora. A app tem de estar aberta; o agendador não inicia o Windows nem acorda um computador suspenso.

## Controlo remoto

1. Em **Preferências → Controlo remoto**, ativa o painel, escolhe uma porta e copia a chave. Guarda as preferências.
2. Usa **Opções → Abrir controlo remoto** para abrir o painel neste computador.
3. Para outro computador/telemóvel, ativa **Permitir outros dispositivos na rede local / VPN** e abre um dos endereços mostrados nas preferências.
4. Introduz a chave no painel. Ela fica apenas na sessão desse navegador. **Gerar nova chave** invalida a anterior depois de guardar.

O controlo remoto está desativado por defeito e, ao ativar, aceita apenas ligações deste computador, até escolheres o acesso pela rede. Usa HTTP, por isso deve ser usado numa rede de confiança ou através de VPN, sem expor diretamente a porta à Internet. Pode ser necessário permitir a porta na firewall do Windows. A app não abre essa porta no router nem cria regras na firewall. Não exige administrador nem conta externa.

O painel permite adicionar magnets, URLs HTTP(S) de torrents ou enviar `.torrent`, iniciar/forçar/pausar/parar/verificar/remover torrents, alterar a fila, definir prioridades por ficheiro, ajustar os limites de largura de banda e consultar/atualizar/transferir itens RSS. As novas transferências remotas usam a pasta predefinida da aplicação. A remoção mantém os ficheiros.

## Atualizar uma instalação portátil

Fecha a app, extrai o novo ZIP e substitui o executável e os ficheiros de documentação/licenças na pasta existente. **Mantém as pastas `Data` e `Downloads`**. A sessão anterior é lida automaticamente e as novas preferências recebem valores predefinidos. A chave remota, os feeds, os horários e a fila ficam em `Data` e acompanham a pasta portátil.

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

Botão direito num torrent para abrir a pasta, copiar o magnet, definir etiqueta, verificar, atualizar trackers ou forçar o início. No separador Ficheiros, seleciona ficheiros e usa o botão direito para mudar prioridades. Os contadores são guardados ao alterar a sessão e ao fechar normalmente a aplicação.

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

Os testes de integração criam torrents de dados sintéticos e transferem-nos em loopback: verificam metadados magnet, pausa, persistência, retoma, prioridades, SHA256, duplicados, remoção sem apagar dados e mudança da pasta portátil. Verificam também a promoção automática da fila após uma transferência real, horários e limites, objetivos de partilha, RSS/Atom/filtros/ETag e autenticação, upload, origem dos pedidos e rotação da chave na API remota real. A CI executa-os em Windows e macOS, compila o `.exe` e abre as janelas em Windows para gerar capturas dos dois temas, RSS, fila, agendador e preferências remotas.

O servidor remoto só é iniciado quando ativado. A fila/agendador usam um temporizador assíncrono de 2 segundos e o RSS verifica feeds vencidos a cada 30 segundos, respeitando o intervalo de cada subscrição. A aplicação principal continua a usar Windows Forms; o painel remoto é HTML/CSS/JavaScript sem frameworks nem recursos externos.

## Licenças e inspiração

O código e os ícones originais desta aplicação são distribuídos sob a [licença MIT](LICENSE). As distribuições do projeto são gratuitas. A licença MIT permite a reutilização por terceiros segundo os seus próprios termos.

As bibliotecas MonoTorrent 3.0.2, Mono.Nat 3.0.0 e ReusableTasks 4.0.0 são MIT, com os avisos de copyright e textos integrais preservados em [licenses/](licenses/). O runtime .NET e os seus componentes mantêm as respetivas licenças e avisos; a publicação inclui os textos fornecidos nos pacotes Microsoft. Consulta [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

Referência visual: [µTorrent Classic](https://www.utorrent.com/desktop/). µTorrent é uma marca dos seus titulares. A inspiração visual não concede direitos sobre essa marca ou sobre o software proprietário. O [REA](https://github.com/morluto/rea) foi considerado como ferramenta de análise; esta implementação não inclui REA nem resulta da descompilação do cliente original.
