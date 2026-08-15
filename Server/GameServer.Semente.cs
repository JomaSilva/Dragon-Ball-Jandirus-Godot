using System.Globalization;
using System.Text.Json;
using Godot;

namespace Jandirus.Server;

/// <summary>
/// A SEMENTE DESTE MUNDO -- o `universo.json`.
///
/// ============================ O PEDIDO, E A SUTILEZA DELE ============================
/// O dono: *"percebi q os NPCS estao sempre nascendo IGUAIS a cada WIPE e o UNIVERSO tb, como se a
/// SEED DELES N MUDASSE"*. Ela nao mudava mesmo: era `public const ulong SeedDoUniverso =
/// 20260802` em `GameServer.Espaco.cs`, uma constante de COMPILACAO. Todo mundo novo era, byte a
/// byte, o mesmo mundo -- os mesmos planetas, os mesmos 148 cidadaos com os mesmos nomes.
///
/// **E o pedido nao e pra quebrar o determinismo -- e pra mudar ONDE a semente nasce.** As duas
/// coisas convivem, e e a diferenca entre:
///   * a semente ser uma CONSTANTE -> todo mundo novo e o mesmo mundo (o defeito);
///   * a semente ser SORTEADA UMA VEZ, quando o mundo nasce, e guardada no save do mundo -> o
///     reinicio devolve o MESMO mundo (o arquivo esta la) e o WIPE, que apaga o arquivo, e o unico
///     gesto que troca de universo.
///
/// Ou seja: **quem troca o mundo e o wipe, e nao o boot**. Um conserto que fizesse o mundo mudar a
/// cada reinicio teria quebrado a lei da casa em vez de cumprir o pedido -- as contas guardam
/// `ZonaSeed`, as obras guardam a zona onde estao de pe e os dominios de conquista guardam o
/// endereco `(Sx, Sy, K)` do planeta. Trocar a semente de um mundo VIVO poe o jogador que deslogou
/// no espaco numa zona que nao existe mais e a casa que ele ergueu num planeta que nunca houve.
/// ==================================================================================
///
/// ============================ UM LUGAR SO DECIDE ============================
/// Este arquivo. `SeedDoUniverso` (em `GameServer.Espaco.cs`) le <see cref="_semente"/> e mais
/// nada; nenhum outro sistema sorteia. A varredura confirma que a raiz e unica -- galaxia, planeta
/// gerado, clima, ceu, berco, povoamento, invasao e embaralho TODOS derivam dela por sal fixo
/// (`Espaco.Misturar`, `SorteioDeNpc.SementeDe`), e o cliente nunca a calcula: ele a recebe pelo
/// fio no login (`GameServer.cs`, `Protocol.S2C`) e usa `cli.SeedDoUniverso` nos 30 pontos dele.
/// Trocar aqui move o universo inteiro de uma vez; nao ha metade que fique parada.
/// ==========================================================================
///
/// ============================ O MUNDO VIVO NAO TROCA DE SEMENTE ============================
/// O save do dono nasceu ANTES deste arquivo existir: ele tem contas, obras, dominios e planetas
/// mortos, e nao tem `universo.json`. Se o boot sorteasse uma semente nova pra ele, o mundo dele
/// mudaria debaixo dele -- que e exatamente o que este sistema existe pra impedir.
///
/// Entao a regra de adocao e: **pasta com alguma coisa dentro = mundo que ja existe = fica com a
/// <see cref="SementeLegada"/>** (a constante de sempre, 20260802), escrita no arquivo agora pra a
/// pergunta nunca mais ser feita. So sorteia quem nasce numa pasta vazia -- primeiro boot de
/// verdade, ou o boot seguinte a um wipe.
/// ======================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// A SEMENTE QUE O MUNDO TINHA QUANDO ELA ERA CONSTANTE.
	///
	/// Ela nao e mais o padrao de nada: e o valor que um mundo JA EXISTENTE (save sem
	/// `universo.json`) adota, pra continuar sendo o mesmo mundo depois desta mudanca. Um mundo novo
	/// so cai nela por acaso, com chance de 1 em 2^64.
	/// </summary>
	public const ulong SementeLegada = 20260802;

	/// <summary>
	/// A semente deste mundo. Comeca na legada e nao em zero de proposito: se alguem ler a semente
	/// antes de <see cref="CarregarSemente"/> rodar (uma bancada que forja um servidor sem disco),
	/// a resposta e um universo VALIDO e nao a zona de espaco de `seed 0` -- e zero e valor proibido
	/// aqui, porque o robo de navegacao afirma `cli.SeedDoUniverso != 0` como sinal de "chegou".
	/// </summary>
	private ulong _semente = SementeLegada;

	/// <summary>
	/// A semente que esta na memoria E o que esta gravado no `universo.json` desta pasta?
	///
	/// E o contador do inscrito na limpeza -- e a resposta pra "o wipe alcancou a semente?". Depois
	/// do `Zerar` ela e `false` (o mundo novo ja foi sorteado, mas ainda nao tem arquivo); depois de
	/// <see cref="SalvarSemente"/> ou <see cref="CarregarSemente"/>, `true`.
	/// </summary>
	private bool _sementeNoDisco;

	private string CaminhoDaSemente =>
		System.IO.Path.Combine(_store?.Pasta ?? ".", "universo.json");

	/// <summary>
	/// O QUE VAI PRO `universo.json`. Tipo proprio, como o `LivroDeReputacao`: o formato do arquivo
	/// nao pode ser um detalhe de quem escreve.
	/// </summary>
	private sealed class FichaDoUniverso
	{
		/// <summary>
		/// A SEMENTE EM HEXA DE LARGURA FIXA (`0x` + 16 digitos), e a largura fixa nao e enfeite.
		///
		/// A bancada da limpeza compara o mundo por RETRATO, e o retrato de disco dela e
		/// `nome (tamanho em bytes)`. Uma semente escrita em decimal muda de comprimento com o
		/// valor (`7` contra `18446744073709551615`), entao o retrato "primeiro boot" e o retrato
		/// "depois da limpeza" ficariam diferentes **pelo numero de digitos** -- uma bancada
		/// vermelha por um motivo que nao tem nada a ver com o que ela mede. Em hexa de 16, todo
		/// arquivo tem exatamente o mesmo tamanho.
		///
		/// TEXTO e nao numero tambem por leitura: quem abre o arquivo ve `0x00000000013527C2` e
		/// entende que aquilo e um identificador, nao uma contagem de alguma coisa.
		/// </summary>
		public string Semente = "";

		/// <summary>Quando este mundo nasceu (Unix ms). So pro dono saber a idade do universo dele.</summary>
		public long Nascido;
	}

	// =====================================================================
	// O SORTEIO
	// =====================================================================
	/// <summary>
	/// SORTEIA UMA SEMENTE DE MUNDO.
	///
	/// Criptografica e nao `Random`: um `Random` sem semente e semeado pelo relogio, e dois
	/// servidores que subissem no mesmo milissegundo (o que acontece em maquina de teste) nasceriam
	/// no mesmo universo -- que e o defeito de novo, so que mais dificil de ver.
	///
	/// ZERO E EXCLUIDO. Nao por superstic: `Client/RoboDeNav.cs` afirma `cli.SeedDoUniverso != 0`
	/// como prova de que a semente CHEGOU no login. Uma semente zero valida faria essa bancada
	/// reprovar uma vez a cada 2^64 mundos -- uma falha que ninguem jamais conseguiria reproduzir.
	/// </summary>
	private static ulong SortearSemente()
	{
		Span<byte> b = stackalloc byte[8];
		ulong s;
		do
		{
			System.Security.Cryptography.RandomNumberGenerator.Fill(b);
			s = BitConverter.ToUInt64(b);
		}
		while (s == 0);
		return s;
	}

	// =====================================================================
	// DISCO
	// =====================================================================
	/// <summary>
	/// A SEMENTE DESTE MUNDO, decidida UMA VEZ. Chamada no boot (dentro do `CarregarVisual`, antes
	/// de qualquer sistema que consulte o universo) e na recarga da bancada da limpeza.
	///
	/// Os tres caminhos, nesta ordem, e a ordem e a regra:
	///   1. **O ARQUIVO MANDA.** Existe `universo.json` -> este mundo ja tem semente, e reiniciar o
	///      servidor devolve o mesmo mundo. E o caso comum, e o unico que roda todo dia.
	///   2. **PASTA COM COISA DENTRO -> MUNDO VELHO.** Sem arquivo, mas com save de alguem (conta,
	///      obra, dominio...): o mundo ja existe e a semente dele SEMPRE foi a
	///      <see cref="SementeLegada"/>. Ele fica com ela, e o arquivo passa a existir. Ver o
	///      cabecalho: trocar a semente de um mundo vivo apaga o chao debaixo dos jogadores.
	///   3. **PASTA VAZIA -> MUNDO NOVO.** Sorteia e grava. Primeiro boot da maquina, ou o boot
	///      depois de um wipe.
	///
	/// O `admin.log` nao conta como "coisa dentro": ele e o unico arquivo que ATRAVESSA a limpeza
	/// (ver `GameServer.Limpeza.cs`), entao conta-lo faria todo mundo pos-wipe ser lido como "mundo
	/// velho" e o wipe deixaria de trocar de universo -- que e o pedido inteiro.
	/// </summary>
	private void CarregarSemente()
	{
		if (_store == null)
		{
			// SERVIDOR SEM DISCO (bancada que forja um `GameServer` na mao). Sorteia pra ele nao
			// rodar todo mundo no mesmo universo, e diz em voz alta que aquilo nao persiste.
			_semente = SortearSemente();
			_sementeNoDisco = false;
			GD.Print($"[universo] sem pasta de saves -- semente de sessao {Hexa(_semente)} (nao persiste)");
			return;
		}

		// ---------------------------------------------------------- 1. o arquivo manda
		if (System.IO.File.Exists(CaminhoDaSemente))
		{
			try
			{
				FichaDoUniverso? f = JsonSerializer.Deserialize<FichaDoUniverso>(
					System.IO.File.ReadAllText(CaminhoDaSemente),
					new JsonSerializerOptions { IncludeFields = true });

				if (f != null && LerHexa(f.Semente) is { } lida && lida != 0)
				{
					_semente = lida;
					_sementeNoDisco = true;
					GD.Print($"[universo] semente {Hexa(_semente)} ({_semente}) lida do universo.json "
						   + "-- reiniciar NAO muda o mundo");
					return;
				}
				GD.PushWarning("[universo] universo.json sem semente legivel -- tratando como mundo antigo");
			}
			catch (Exception e)
			{
				// ARQUIVO ILEGIVEL NAO SORTEIA. Um `universo.json` corrompido num mundo POVOADO nao
				// pode virar um mundo novo por conta propria: o custo do erro e o mundo do dono. Cai
				// no caminho 2, que preserva.
				GD.PushWarning($"[universo] universo.json ilegivel ({e.Message}) -- tratando como mundo antigo");
			}

			_semente = SementeLegada;
			_sementeNoDisco = false;
			SalvarSemente();
			return;
		}

		// ---------------------------------------------------------- 2 e 3. quem e este mundo?
		bool mundoVelho = false;
		try
		{
			foreach (string caminho in _store.TodosOsArquivos())
			{
				string nome = System.IO.Path.GetFileName(caminho);
				if (string.Equals(nome, "admin.log", StringComparison.OrdinalIgnoreCase)) continue;
				mundoVelho = true;
				break;
			}
		}
		catch (Exception e) { GD.PushWarning($"[universo] nao consegui olhar a pasta de saves: {e.Message}"); }

		if (mundoVelho)
		{
			_semente = SementeLegada;
			GD.Print($"[universo] save ANTIGO sem universo.json -- este mundo MANTEM a semente de "
				   + $"sempre ({SementeLegada}). Nada muda pra quem ja joga aqui.");
		}
		else
		{
			_semente = SortearSemente();
			GD.Print($"[universo] mundo NOVO -- semente sorteada {Hexa(_semente)} ({_semente})");
		}

		_sementeNoDisco = false;
		SalvarSemente();
	}

	/// <summary>
	/// GRAVA A SEMENTE. Mesma escrita atomica do `reputacao.json` (temporario + renomeia): um
	/// processo derrubado no meio deixaria o mundo sem endereco, e o boot seguinte leria "mundo
	/// antigo" e devolveria a semente legada -- um universo trocado por uma queda de energia.
	/// </summary>
	private void SalvarSemente()
	{
		if (_store == null) return;
		try
		{
			var f = new FichaDoUniverso { Semente = Hexa(_semente), Nascido = NowMs() };
			string tmp = CaminhoDaSemente + ".tmp";
			System.IO.Directory.CreateDirectory(_store.Pasta);
			System.IO.File.WriteAllText(tmp, JsonSerializer.Serialize(f,
				new JsonSerializerOptions { IncludeFields = true, WriteIndented = true }));
			System.IO.File.Move(tmp, CaminhoDaSemente, overwrite: true);
			_sementeNoDisco = true;
		}
		catch (Exception e) { GD.PushWarning($"[universo] nao deu pra salvar universo.json: {e.Message}"); }
	}

	/// <summary>
	/// O `Zerar` do inscrito na limpeza: **o mundo novo ja nasce com outra semente, na memoria**.
	///
	/// Nao basta o arquivo sumir com a vassoura. O cabecalho de `GameServer.Limpeza.cs` diz por que:
	/// *"Disco limpo com cache sujo e um mundo que mente ate o proximo reinicio"* -- sem esta linha,
	/// o admin limparia o servidor, continuaria dentro do MESMO universo, e o primeiro cidadao do
	/// mundo "novo" nasceria com a semente do mundo anterior.
	///
	/// GRAVAR NAO E AQUI, e a diferenca importa: o `Zerar` roda no passo 3 da limpeza e o disco so e
	/// varrido no passo 4, entao um save daqui seria apagado logo em seguida. Quem grava e o
	/// <see cref="AdminLimparServidor"/>, depois da vassoura passar.
	/// </summary>
	private void ZerarSemente()
	{
		_semente = SortearSemente();
		_sementeNoDisco = false;
		GD.Print($"[universo] wipe: mundo novo, semente {Hexa(_semente)} ({_semente})");
	}

	// =====================================================================
	// FORMATO
	// =====================================================================
	/// <summary>`0x` + 16 digitos, sempre -- ver <see cref="FichaDoUniverso.Semente"/>.</summary>
	private static string Hexa(ulong s) => "0x" + s.ToString("X16", CultureInfo.InvariantCulture);

	/// <summary>Le o que <see cref="Hexa"/> escreveu. Aceita sem o `0x` pra quem editar na mao.</summary>
	private static ulong? LerHexa(string? texto)
	{
		if (texto is not { Length: > 0 }) return null;
		string t = texto.Trim();
		if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) t = t[2..];
		return ulong.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong v) ? v : null;
	}
}
