using Godot;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// AS ABAS SENSE E SCAN -- quem esta por perto e quao forte: a leitura de Ki (relativa, sem numero) e a
/// do scouter (exata). Sao a MESMA aba com dois nomes, como no original (`HtmlUI.dm:360-419`): o scouter
/// ligado troca o nome e a leitura.
///
/// O CLIENTE SO DESENHA O QUE VEIO. A lista nasce no servidor (`GameServer.Sentidos.cs`), ja passada pelo
/// sigilo: no modo Sense o BP absoluto chega como NaN e a linha "Battle Power" simplesmente nao existe;
/// no modo Scan ele chega e e escrito. Nao ha conta de poder aqui -- nao ha de onde tirar.
///
/// UM CARTAO POR PRESENCA: cabecalho com o nome (ou "??? (assinatura)" pra quem nao se conhece,
/// `HtmlUI.dm:374`) e a pilula do alcance; a barra do poder relativo (ou o BP exato); a barra de vida
/// (so perto); a distancia e o rumo (o `get_dist`/`sense_dir_word`); o lugar (so na galaxia).
///
/// (2026-10-19) A BUSCA E O RASTRO -- ver `RastroDoSense`: a caixa "procurar pessoa pelo nome..." filtra os cartoes, e o NOME
/// de cada cartao e clicavel: segue aquela presenca (o selo e a seta do HUD, `RastroNaTela`), e clicar de novo para. O nome
/// continua sendo um `Label` -- e por ele que os robos acham o cartao (`RoboDeAlunos.PoderNaAbaSense`).
/// </summary>
public partial class MenuJogo
{
	/// <summary>
	/// A ASSINATURA DO PACOTE DE SENTIDOS, sem tocar no `_Ready`/`_ExitTree` de `MenuJogo.cs` (arquivo
	/// compartilhado entre as frentes): o `_Notification` recebe o READY e o EXIT_TREE do MESMO node, e e
	/// o unico gancho que um arquivo parcial tem sem sobrescrever o que o outro ja sobrescreveu. Metodo
	/// NOMEADO e `-=` na saida, pela licao das assinaturas vazadas do relog: o `GameClient` sobrevive ao
	/// logout, e um assinante morto acordaria a cada pacote.
	///
	/// ============================ POR QUE ELA PRECISA EXISTIR ============================
	/// O menu se redesenha a cada `SheetUpdated` -- mas o pacote de ficha SO SAI QUANDO A FICHA MUDA
	/// (`TickFichas`: "num servidor parado isso e zero trafego"). Um jogador parado, olhando a aba Sense,
	/// nao recebe ficha nenhuma, e a lista que chegou ficaria na memoria sem nunca virar tela. A bancada
	/// `--diagabas` pegou exatamente isso: 19 presencas no cliente e "0" na faixa.
	/// ==================================================================================
	/// </summary>
	public override void _Notification(int what)
	{
		base._Notification(what);
		if (GameClient.Instance is not { } cli) return;
		// A FREQUENCIA DO RADIO entra pelo mesmo gancho e pelo mesmo motivo (ver `MenuJogo.Radio.cs`): ela chega
		// num pacote proprio, e parado na aba Equip nenhum pacote de ficha a traria pra tela.
		// (2026-10-19) O RASTRO TAMBEM: comecar, trocar ou parar de seguir alguem acende o cartao dele e o do rastro.
		if (what == NotificationReady) { cli.SentidosMudaram += AoSentidos; cli.RadioMudou += AoRadio; RastroDoSense.Mudou += AoSentidos; }
		else if (what == NotificationExitTree) { cli.SentidosMudaram -= AoSentidos; cli.RadioMudou -= AoRadio; RastroDoSense.Mudou -= AoSentidos; }
	}

	/// <summary>Mesma regra do `AoConhecidos`: so remonta se a aba aberta e a que desenha isto.</summary>
	private void AoSentidos() { if (Visible && (_aba == "Sense" || _aba == "Scan")) Redesenhar(); }

	private void AbaSentidos(SheetState f)
	{
		GameClient? cli = GameClient.Instance;
		List<Protocol.PresencaState> lista = cli?.Sentidos ?? [];
		bool scan = cli?.SentidosSaoDoScouter ?? false;

		// A FAIXA: quantas presencas, e de que jeito elas foram lidas.
		// (2026-10-18) O SENSE TEM CINCO NIVEIS: a faixa diz em qual o meu esta, e ate onde ele alcanca (`SenseEmNiveis`).
		int nivel = Math.Max(cli?.NivelDoSense ?? 1, 1);
		Faixa(scan ? "Leituras" : "Presenças", $"{lista.Count}",
			  scan ? "leitura exata do scouter (até 100 milhões) · quem está neste mundo"
				   : $"Sense nível {nivel} de {Jandirus.Core.Skills.SenseEmNiveis.NivelMaximo} · alcance: "
					+ Jandirus.Core.Skills.SenseEmNiveis.NomeDoAlcance(nivel), Tema.Ki);

		// (2026-10-19) O RASTRO EM CURSO vem antes de tudo -- inclusive com a lista vazia, que e justamente quando ele se perdeu e
		// alguem quer parar de segui-lo.
		if (RastroDoSense.Ativo) CartaoDoRastro(lista);
		CartaoDaMaestriaDaTransmissao();   // (2026-10-23) a maestria do Instant Transmission -- ver MenuJogo.MaestriaDaTransmissao.cs

		if (lista.Count == 0)
		{
			VBoxContainer vazio = Cartao(scan ? "Scouter" : "Leitura de Ki");
			// as duas frases do DM ("You sense no notable presences." :396, "Nenhuma leitura..." :417)
			Nota(scan ? "Nenhuma leitura de poder relevante na área." : "Você não sente nenhuma presença notável.", vazio);
			return;
		}

		CaixaDeBuscaDoSense();
		int mostradas = 0;
		for (int i = 0; i < lista.Count; i++)
			if (RastroDoSense.CasaComABusca(lista[i], _buscaDoSense)) { CartaoDePresenca(lista[i], i); mostradas++; }
		if (mostradas == 0)
			Nota($"Ninguém chamado \"{_buscaDoSense.Trim()}\" no alcance do seu {(scan ? "scouter" : "Sense")}.", Cartao("Busca"));
	}

	private void CartaoDePresenca(Protocol.PresencaState p, int linha)
	{
		bool seguido = RastroDoSense.EhOAlvo(p);
		// CHEFE E DESTAQUE (borda laranja): e o corpo que se olha primeiro -- e a presenca seguida tambem.
		VBoxContainer c = Cartao("", destaque: p.Chefe || seguido);
		bool conhecido = p.Nome.Length > 0;
		string nome = conhecido ? p.Nome : p.Assinatura.Length > 0 ? $"??? ({p.Assinatura})" : "???";
		// (2026-10-18) os cinco alcances do Sense -- a pilula diz de que distancia a presenca foi sentida
		(string Texto, Color Cor) alcance = p.Alcance switch
		{
			<= 1 => (Jandirus.Core.Skills.SenseEmNiveis.NomeDoAlcance(1), Tema.Bom),
			2 => (Jandirus.Core.Skills.SenseEmNiveis.NomeDoAlcance(2), Tema.Bom),
			3 => (Jandirus.Core.Skills.SenseEmNiveis.NomeDoAlcance(3), Tema.Ki),
			4 => (Jandirus.Core.Skills.SenseEmNiveis.NomeDoAlcance(4), Tema.Destaque),
			_ => (Jandirus.Core.Skills.SenseEmNiveis.NomeDoAlcance(5), Tema.Destaque),
		};
		HBoxContainer cabecalho = Cabecalho(nome, conhecido ? Tema.Texto : Tema.TextoFraco, alcance, (p.Chefe ? "CHEFE" : "", Tema.Perigo),
											 (seguido ? "RASTREANDO" : "", Tema.Ki));
		if (cabecalho.GetChild(0) is Label titulo) NomeQueSegue(titulo, p, linha);
		c.AddChild(cabecalho);

		// O NUMERO SO EXISTE NO SCAN: no Sense ele chega NaN e a linha nao nasce (o sigilo e do servidor;
		// aqui so se respeita a ausencia).
		if (!double.IsNaN(p.Bp)) Linha("Battle Power", $"{p.Bp:N0}", Tema.Destaque, c);
		// (2026-10-18) NO SCAN, NaN E O VIDRO QUE NAO ALCANCA: acima de 100 milhoes o scouter marca "???" (`Core/Stats/Scouter.cs`).
		else if (GameClient.Instance?.SentidosSaoDoScouter == true) Linha("Battle Power", "???", Tema.TextoFraco, c);

		if (!float.IsNaN(p.PoderRelativo)) LinhaDoPoderRelativo(p.PoderRelativo, c);

		if (p.Distancia != Protocol.DistanciaDesconhecida)
			Linha("distância", $"{p.Distancia} tiles · direção {Protocol.NomeDoRumo(p.Rumo)}", null, c);

		if (p.X >= 0 && p.Y >= 0) Linha("posição", $"({p.X}, {p.Y})", null, c);

		// o `(?,?,z[D.z])` do DM: so o lugar, sem coordenada
		if (p.Zona.Length > 0) Linha("onde", $"(?, ?, {p.Zona})", null, c);

		// (2026-10-21) O BOTAO DO INSTANT TRANSMISSION. O servidor manda o nivel (0 = sem a skill) e o id do corpo:
		// o nome nao distingue homonimo. No Scan o botao nao aparece -- a aba que o dono pediu e a do Sense.
		// O texto e o verbo, "Teletransportar" (pedido do dono, 2026-10-21), e nao o nome da tecnica.
		int nivelIt = GameClient.Instance?.NivelDaTransmissao ?? 0;
		if (GameClient.Instance?.SentidosSaoDoScouter != true && nivelIt > 0 && p.Corpo != 0 && p.Alcance <= nivelIt)
		{
			var ir = new Button
			{
				Text = "Teletransportar",
				FocusMode = Control.FocusModeEnum.None,
			};
			ir.SetMeta("teletransporte", p.Corpo);
			int corpo = p.Corpo;
			ir.Pressed += () => GameClient.Instance?.SendHabilidade("Instant_Transmission:#" + corpo);
			c.AddChild(ir);
		}
	}

	/// <summary>O rotulo da linha do poder relativo. As bancadas a procuram por aqui (e pelo metadado `linha`).</summary>
	internal const string RotuloDoPoderRelativo = "poder relativo";

	/// <summary>
	/// A LINHA DO PODER RELATIVO -- "quanto essa pessoa tem do meu poder", a unica leitura de forca alheia de quem nao
	/// usa scouter: acima de 100% o outro e mais forte que eu, e a barra enche e fica vermelha.
	///
	/// UMA FUNCAO PRAS DUAS TELAS que a desenham: o cartao de presenca desta aba e o cartao de mestre/aluno da aba do
	/// discipulado (`MenuJogo.Alunos.cs`), que rele a MESMA lista do sentido de Ki. Duas copias desta linha seriam
	/// duas leituras de forca no jogo -- e a regra e que haja uma so.
	/// </summary>
	private static void LinhaDoPoderRelativo(float porcento, Control pai)
	{
		Color cor = porcento > 100 ? Tema.Perigo : Tema.Ki;
		LinhaComBarra(RotuloDoPoderRelativo, $"{porcento:0}%", Math.Min(porcento / 100.0, 1), cor, pai,
					  porcento > 100 ? Tema.Perigo : null);
	}

	/// <summary>
	/// O PEDACO DESTA ABA NA ASSINATURA DE CACHE (ver `MenuJogo.Assinatura`). Sense/Scan nao tem
	/// assinatura basica ("sem cache"), entao ESTA e a assinatura inteira: o modo e o conteudo da lista,
	/// nos mesmos arredondamentos em que e desenhado. E ela que remonta a aba quando um pacote novo chega
	/// -- sem precisar de assinatura de evento no `_Ready` do menu, porque o menu ja se redesenha a cada
	/// ficha (5 Hz) e so remonta quando isto muda. NUNCA VAZIA: uma lista vazia tambem e um estado, e
	/// devolver "" aqui seria remontar a pagina vazia cinco vezes por segundo.
	/// </summary>
	private string ExtraDaAssinaturaDeSentidos(SheetState f)
	{
		GameClient? c = GameClient.Instance;
		// (2026-10-19) as duas buscas e o rastro -- ver `AssinaturaDaBuscaEDoRastro` (`MenuJogo.Rastro.cs`)
		return $"{(c?.SentidosSaoDoScouter == true ? 'S' : 'K')}{c?.NivelDoSense}|{c?.NivelDaTransmissao}{c?.Transmissao}|{AssinaturaDaBuscaEDoRastro()}|" + string.Join(',', (c?.Sentidos ?? []).Select(p =>
			$"{p.Nome}/{p.Assinatura}/{p.Alcance}/{p.PoderRelativo:0}/{p.Bp:0}/{p.Distancia}/{p.Rumo}/{p.X}/{p.Y}/{p.Zona}/{p.Chefe}/{p.Corpo}"));
	}
}
