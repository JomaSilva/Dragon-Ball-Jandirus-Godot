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
		if (what == NotificationReady) cli.SentidosMudaram += AoSentidos;
		else if (what == NotificationExitTree) cli.SentidosMudaram -= AoSentidos;
	}

	/// <summary>Mesma regra do `AoConhecidos`: so remonta se a aba aberta e a que desenha isto.</summary>
	private void AoSentidos() { if (Visible && (_aba == "Sense" || _aba == "Scan")) Redesenhar(); }

	private void AbaSentidos(SheetState f)
	{
		GameClient? cli = GameClient.Instance;
		List<Protocol.PresencaState> lista = cli?.Sentidos ?? [];
		bool scan = cli?.SentidosSaoDoScouter ?? false;

		// A FAIXA: quantas presencas, e de que jeito elas foram lidas.
		Faixa(scan ? "Leituras" : "Presenças", $"{lista.Count}",
			  scan ? "leitura exata do scouter · quem está neste mundo"
				   : "por alcance: perto · neste mundo · na galáxia", Tema.Ki);

		if (lista.Count == 0)
		{
			VBoxContainer vazio = Cartao(scan ? "Scouter" : "Leitura de Ki");
			// as duas frases do DM ("You sense no notable presences." :396, "Nenhuma leitura..." :417)
			Nota(scan ? "Nenhuma leitura de poder relevante na área." : "Você não sente nenhuma presença notável.", vazio);
			return;
		}

		foreach (Protocol.PresencaState p in lista) CartaoDePresenca(p);
	}

	private void CartaoDePresenca(Protocol.PresencaState p)
	{
		// CHEFE E DESTAQUE (borda laranja): e o corpo que se olha primeiro.
		VBoxContainer c = Cartao("", destaque: p.Chefe);
		bool conhecido = p.Nome.Length > 0;
		string nome = conhecido ? p.Nome : p.Assinatura.Length > 0 ? $"??? ({p.Assinatura})" : "???";
		(string Texto, Color Cor) alcance = p.Alcance switch
		{
			1 => ("perto", Tema.Bom),
			2 => ("neste mundo", Tema.Ki),
			_ => ("na galáxia", Tema.Destaque),
		};
		c.AddChild(Cabecalho(nome, conhecido ? Tema.Texto : Tema.TextoFraco, alcance, (p.Chefe ? "CHEFE" : "", Tema.Perigo)));

		// O NUMERO SO EXISTE NO SCAN: no Sense ele chega NaN e a linha nao nasce (o sigilo e do servidor;
		// aqui so se respeita a ausencia).
		if (!double.IsNaN(p.Bp)) Linha("Battle Power", $"{p.Bp:N0}", Tema.Destaque, c);

		// O PODER RELATIVO: acima de 100% o outro e mais forte que eu -- a barra enche e fica vermelha.
		if (!float.IsNaN(p.PoderRelativo))
		{
			Color cor = p.PoderRelativo > 100 ? Tema.Perigo : Tema.Ki;
			LinhaComBarra("poder relativo", $"{p.PoderRelativo:0}%", Math.Min(p.PoderRelativo / 100.0, 1), cor, c,
						  p.PoderRelativo > 100 ? Tema.Perigo : null);
		}

		if (p.Hp != Protocol.HpDesconhecido)
			LinhaComBarra("vida", $"{p.Hp}%", p.Hp / 100.0, BodyDoll.Cor(p.Hp), c, BodyDoll.Cor(p.Hp));

		if (p.Distancia != Protocol.DistanciaDesconhecida)
			Linha("distância", $"{p.Distancia} tiles · direção {Protocol.NomeDoRumo(p.Rumo)}", null, c);

		if (p.X >= 0 && p.Y >= 0) Linha("posição", $"({p.X}, {p.Y})", null, c);

		// o `(?,?,z[D.z])` do DM: so o lugar, sem coordenada
		if (p.Zona.Length > 0) Linha("onde", $"(?, ?, {p.Zona})", null, c);
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
		return $"{(c?.SentidosSaoDoScouter == true ? 'S' : 'K')}|" + string.Join(',', (c?.Sentidos ?? []).Select(p =>
			$"{p.Nome}/{p.Assinatura}/{p.Alcance}/{p.PoderRelativo:0}/{p.Bp:0}/{p.Hp}/{p.Distancia}/{p.Rumo}/{p.X}/{p.Y}/{p.Zona}/{p.Chefe}"));
	}
}
