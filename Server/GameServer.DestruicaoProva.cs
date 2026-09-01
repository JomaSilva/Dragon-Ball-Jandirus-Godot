using System.IO;
using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Races;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// AS PROVAS DA MORTE DE PLANETA (`--planetateste`, a segunda metade).
///
/// ============================ POR QUE UM SEGUNDO ARQUIVO ============================
/// O `GameServer.DestruicaoTeste.cs` responde *"o sistema faz o que o dono pediu?"*, e ja tem 153
/// checagens verdes dizendo que sim. Este arquivo responde a pergunta que sobra, e ela e a unica que
/// importa depois que tudo esta verde:
///
///     **"e como e que a gente sabe que aquelas 153 nao ficariam verdes com o sistema quebrado?"**
///
/// Verde nao prova nada sozinho. Este projeto ja pagou caro por cinco familias de defeito com nome e
/// sobrenome -- a regra ligada num chamador e esquecida no outro; o dado escrito sem consumidor; a
/// bancada que mede a INTENCAO (o campo escrito) em vez do RESULTADO (o mundo mudado); a bancada que
/// NASCE dentro do estado e nunca testa a entrada nele; e a afirmacao de um lado so, que fica verde
/// num sistema morto. As familias daqui sao, uma a uma, a negacao de uma dessas.
///
/// ============================ AS NOVE PERGUNTAS QUE SO DAQUI SE RESPONDEM ============================
///   1. **A FORMULA E UMA SO, POR CONTAGEM?** Nao "os dois numeros bateram" -- duas implementacoes
///      identicas tambem batem, e e assim que elas passam a divergir seis meses depois. Aqui se
///      CONTA, no fonte: quantas definicoes de `Furia` existem (uma), quantas chamadas de producao
///      existem fora do Core (uma, o envelope) e se os DOIS consumidores passam por ela.
///   2. **E A IGUALDADE, COM NUMERO E NAS DUAS PONTAS DE PRODUCAO?** Quanto de vida o mundo absorveu
///      ate cair (contado tiro a tiro) e quanto de dano a explosao dele aplicou num corpo (medido no
///      membro). Sao o mesmo numero -- e o dono escreveu isso com todas as letras.
///   3. **A RAMPA SOBE, E TODOS OS EFEITOS LEEM A MESMA FRACAO?** Medida em cinco instantes (um
///      valor so nao prova rampa) e depois **quebrada de proposito**: com a fracao travada numa
///      constante, o ceu, o tremor, o chao e a cratera tem que cair TODOS. Um efeito que sobrevivesse
///      teria nocao PROPRIA de intensidade -- o defeito que a fracao unica existe pra impedir.
///   4. **OS 5 MINUTOS SAO 5 MINUTOS?** Com o relogio adiantado pelo tique de PRODUCAO, segundo a
///      segundo, e com a outra metade: aos 309 s o mundo **ainda esta la**. So "explodiu no fim"
///      ficaria verde num sistema que explode no segundo zero.
///   5. **O SIGILO DO K5 VAZA POR ALGUM LUGAR?** Tres camadas, e nenhuma delas e "li o codigo": a
///      FALA (varrida digito por digito), o PACOTE (os bytes que saem do `MandarMortos`, varridos
///      janela por janela atras dos numeros proibidos) e o FONTE do cliente (que nao conhece uma
///      unica das grandezas secretas). E a varredura e ela mesma testada com um pacote ENVENENADO --
///      um crivo que nunca corta e indistinguivel de crivo nenhum.
///   6. **QUANTOS TIROS, DE VERDADE?** A tabela do K4 **medida** -- tiros contados um a um no caminho
///      de producao ate o mundo cair --, e nao a divisao `vida/dano` feita no papel.
///   7. **ZERAR A VIDA ENTRA PELA MESMA PORTA?** Por contagem de chamadores no fonte, com os seis
///      caminhos de producao listados por nome.
///   8. **O QUE ACONTECE COM O RESTO DO JOGO?** O berco (as 24 racas, medidas uma a uma, com a
///      cascata Terra -> Namek -> Vegeta), o dominio, o povoamento -- e, do outro lado, os sistemas
///      que **nao** consultam a morte e que o dono precisa saber que nao consultam.
///   9. **E CADA FAMILIA SABE FICAR VERMELHA?** Toda uma declara os defeitos que existe pra pegar, a
///      bancada INJETA cada um (pelas <see cref="SondasDaAgonia"/> ou por uma leitura adulterada do
///      fonte) e exige o vermelho. Verde com o defeito dentro sai como `CEGA` -- isso e um buraco de
///      cobertura, e nao um sucesso.
/// ====================================================================================================
///
/// **TUDO RODA DENTRO DO <see cref="PalcoDeMortes"/>**: estas familias matam planetas de verdade (a
/// Terra, Namek, Vegeta, Arlia) dezenas de vezes, e o palco e o que garante que nada disso encoste no
/// `planetas-mortos.json` do dono.
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// OS BYTES DO `S2C.Mortos`, como eles saem do <see cref="MandarMortos"/>.
	///
	/// E o unico pacote que carrega estado de morte de planeta, e a familia do sigilo precisa afirmar
	/// uma coisa sobre ELE e nao sobre o codigo que o escreve: *"nada da ferida viaja"*. Ler o fonte
	/// responderia "o programador nao quis mandar"; ler os bytes responde "nao foi".
	/// </summary>
	internal static List<byte[]>? EscutaDeMortos;

	/// <summary>
	/// COMO ESTA BANCADA LE O FONTE. Nulo = leitura de verdade; a bancada troca por uma leitura
	/// adulterada pra provar que as familias de CONTAGEM tambem sabem reprovar -- sem isso, "existe
	/// uma so definicao" seria uma frase que nunca ficou vermelha. Mesmo truque do `_vacFonteMutante`
	/// da bancada do vacuo.
	/// </summary>
	private Func<string, string>? _fonteMutanteDaMorte;

	private sealed class ProvaDaMorte
	{
		public required string Nome { get; init; }
		public required string Frase { get; init; }
		public required Action<Checagem> Provas { get; init; }
		public required List<(string Nome, Action<SondasDaAgonia> Injetar)> Defeitos { get; init; }
	}

	// =====================================================================
	// O MOTOR
	// =====================================================================
	/// <summary>
	/// Roda cada familia SA (contando e imprimindo) e depois uma vez por defeito declarado, com as
	/// sondas trocadas por baixo das MESMAS provas. O que importa na rodada do defeito nao e o placar
	/// dela e sim se ela ficou vermelha -- por isso a rodada mutante e muda.
	/// </summary>
	private (int Ok, int Falhas, int Cegos, List<string> Buracos) RodarAsProvasDaMorte(ServerPlayer pl)
	{
		GD.Print("\n===== PROVAS DA MORTE DE PLANETA -- verde E sabendo ficar vermelha =====");

		// ============================ O OPERADOR PRECISA ESTAR VIVO ============================
		// Nao e cortesia: `AlguemOnline()` e regra de PRODUCAO ("servidor vazio adia o pavio"), e
		// estas familias rodam DEPOIS das treze de cima -- que matam, nocauteiam e evacuam o unico
		// corpo com teclado do servidor. Com ele morto, a familia das bordas mediria um pavio
		// congelado e acusaria um defeito que nao existe. A bancada de cima ja pagou por isso uma vez
		// e a nota dela esta la; aqui a mesma linha, pelo mesmo motivo, um nivel adiante.
		// ==================================================================================
		pl.Ficha.dead = false;
		pl.Ficha.KO = false;
		pl.Combate?.Corpo.Curar(1e9);
		pl.Combate?.SincronizarVida();
		pl.Ficha.Tick(agoraMs: NowMs());

		List<ProvaDaMorte> familias =
		[
			AFormulaEUmaSo(),
			ADanoIgualAVida(),
			ARampaEUmaFracaoSo(pl),
			OsCincoMinutos(),
			OSigiloDoK5(pl),
			ATabelaMedidaDoK4(),
			AMesmaPortaDoK6(),
			AsConsequenciasNoResto(),
			AsBordasDoK(),
			ORescaldoDoMundo(),
		];

		int ok = 0, falhas = 0, cegos = 0, defeitos = 0;
		var buracos = new List<string>();

		foreach (ProvaDaMorte f in familias)
		{
			GD.Print($"\n  === {f.Nome} ===");
			GD.Print($"      \"{f.Frase}\"");

			int fOk = 0, fFalhas = 0;
			void Imprimindo(string nome, bool cond, string detalhe = "")
			{
				if (cond) { fOk++; GD.Print($"    OK   {nome}   {detalhe}"); }
				else { fFalhas++; GD.PrintErr($"    FALHA {nome}   {detalhe}"); }
			}

			RodarUmaProva(f, Imprimindo);
			ok += fOk; falhas += fFalhas;

			if (f.Defeitos.Count == 0) continue;
			GD.Print("    -- e ela reprova assim:");
			foreach ((string nome, Action<SondasDaAgonia> injetar) in f.Defeitos)
			{
				defeitos++;
				var vermelhas = new List<string>();
				void Muda(string n, bool cond, string _ = "") { if (!cond) vermelhas.Add(n); }

				var mutante = new SondasDaAgonia();
				injetar(mutante);
				_sondasDaAgonia = mutante;

				RodarUmaProva(f, Muda);

				if (vermelhas.Count == 0)
				{
					cegos++;
					GD.PrintErr($"       [CEGA] {nome}");
					GD.PrintErr("              ...a familia continuou VERDE com o defeito dentro.");
					buracos.Add($"{f.Nome}: cega para \"{nome}\"");
				}
				else
				{
					GD.Print($"       [pega] {nome}  ->  {vermelhas.Count} prova(s) em vermelho");
					foreach (string v in vermelhas.Take(6)) GD.Print($"              - {CurtoNaProva(v)}");
				}
			}
		}

		GD.Print("\n===== PLACAR DAS PROVAS =====");
		GD.Print($"      familias           : {familias.Count}");
		GD.Print($"      provas             : {ok + falhas}   ({ok} verdes, {falhas} vermelhas)");
		GD.Print($"      defeitos injetados : {defeitos}   ({defeitos - cegos} pegos, {cegos} passaram batido)");
		foreach (string b in buracos) GD.PrintErr($"        - {b}");
		return (ok, falhas, cegos, buracos);
	}

	/// <summary>
	/// Uma rodada de uma familia, com o mundo devolvido mesmo quando ela estoura.
	///
	/// O `finally` desfaz TRES coisas -- as sondas, o fonte adulterado e o mundo -- e ele existe
	/// porque uma familia que estourasse no meio de um defeito injetado deixaria o servidor com a
	/// regra quebrada LIGADA. Bancada que estraga o servidor pra medi-lo nao mede mais nada depois.
	/// </summary>
	private void RodarUmaProva(ProvaDaMorte f, Checagem checa)
	{
		try { f.Provas(checa); }
		catch (Exception e)
		{
			// UM DEFEITO QUE FAZ A BANCADA ESTOURAR TAMBEM E UM DEFEITO PEGO -- so nao pode passar
			// despercebido, entao ele sai com o nome da excecao.
			checa($"estourou: {e.GetType().Name} {e.Message}", false,
				  e.StackTrace?.Split('\n').FirstOrDefault()?.Trim() ?? "");
		}
		finally
		{
			_sondasDaAgonia = null;
			_fonteMutanteDaMorte = null;
			LimparAOficinaDaMorte();
		}
	}

	/// <summary>
	/// O MUNDO VOLTA: registro, feridas, mordacas, relogios, ceu, chao e os corpos forjados.
	///
	/// Roda depois de CADA rodada (sa ou mutante), porque a seguinte precisa comecar do mesmo lugar --
	/// foi por nao limpar entre rodadas que a bancada do vacuo colheu dez linhas de aviso que nao eram
	/// dela e acusou um vazamento que nao existia.
	/// </summary>
	private void LimparAOficinaDaMorte()
	{
		foreach (ServerPlayer p in _forjadosDaProva) Recolher(p);
		_forjadosDaProva.Clear();

		_mortos.Limpar();
		_feridasDeMundo.Clear();
		_faleiDoMundo.Clear();
		_proximoTremor.Clear();
		_cargaDoPlanetDestroy.Clear();
		EscutaDeMortos = null;

		foreach (string nome in PlanetasQueEstaFamiliaToca)
		{
			var z = ZoneKey.Premade(nome);
			ForcarClima(z, TipoDeClima.Limpo, 0);
			_cenarioCaido.Remove(nome);
		}
	}

	/// <summary>Os sete pre-feitos destrutiveis -- os unicos que estas familias podem sujar.</summary>
	private static readonly string[] PlanetasQueEstaFamiliaToca =
		["Earth", "Namek", "Vegeta", "Icer", "Arlia", "Arconia", "Makyo_Star"];

	private readonly List<ServerPlayer> _forjadosDaProva = [];

	/// <summary>Corta uma linha longa pro log. Irma da `Curto` da bancada do vacuo, com nome proprio.</summary>
	private static string CurtoNaProva(string s) => s.Length <= 90 ? s : s[..87] + "...";

	/// <summary>Um corpo de prova que a <see cref="LimparAOficinaDaMorte"/> recolhe sozinha.</summary>
	private ServerPlayer ForjarNaProva(ZoneKey zona, string nome, double bp)
	{
		ServerPlayer p = ForjarEm(zona, nome, bp);
		_forjadosDaProva.Add(p);
		return p;
	}

	// =====================================================================
	// AS FERRAMENTAS: ler o fonte, contar, e varrer bytes
	// =====================================================================
	/// <summary>
	/// TODO O FONTE DA CASA, por pasta. `obj/` e `bin/` ficam de fora -- eles guardam copias geradas
	/// do proprio codigo, e conta-las faria "uma unica definicao" virar tres sem que ninguem tenha
	/// escrito nada.
	/// </summary>
	private List<(string Rel, string Texto)> FontesDaCasa(params string[] pastas)
	{
		var saida = new List<(string, string)>();
		string raizDoProjeto = ProjectSettings.GlobalizePath("res://").Replace('\\', '/');

		foreach (string pasta in pastas)
		{
			string raiz = ProjectSettings.GlobalizePath("res://" + pasta);
			if (!Directory.Exists(raiz)) continue;

			foreach (string caminho in Directory.EnumerateFiles(raiz, "*.cs", SearchOption.AllDirectories))
			{
				string rel = caminho.Replace('\\', '/');
				if (rel.Contains("/obj/") || rel.Contains("/bin/")) continue;
				if (rel.StartsWith(raizDoProjeto, StringComparison.OrdinalIgnoreCase))
					rel = rel[raizDoProjeto.Length..];

				try { saida.Add((rel, (_fonteMutanteDaMorte ?? File.ReadAllText)(caminho))); }
				catch (Exception e) { GD.PushWarning($"[prova] nao deu pra ler {rel}: {e.Message}"); }
			}
		}
		return saida;
	}

	/// <summary>
	/// ONDE UMA AGULHA APARECE, **fora de comentario**.
	///
	/// O filtro de comentario nao e preciosismo: `MortePlanetaria.Furia` aparece dezenas de vezes em
	/// `&lt;see cref=...&gt;` neste repo, e conta-las faria a prova "uma unica chamada de producao"
	/// nascer vermelha por causa da propria documentacao que a explica.
	/// </summary>
	private static List<(string Arquivo, int Linha, string Texto)> Ocorrencias(
		IEnumerable<(string Rel, string Texto)> fontes, string agulha)
	{
		var achados = new List<(string, int, string)>();
		foreach ((string rel, string texto) in fontes)
		{
			string[] linhas = texto.Replace("\r\n", "\n").Split('\n');
			for (int i = 0; i < linhas.Length; i++)
			{
				string t = linhas[i].TrimStart();
				if (t.StartsWith("//") || t.StartsWith("*") || t.StartsWith("/*")) continue;
				if (!t.Contains(agulha, StringComparison.Ordinal)) continue;
				achados.Add((rel, i + 1, t));
			}
		}
		return achados;
	}

	/// <summary>Os arquivos que NAO sao bancada -- o codigo que roda no jogo do dono.</summary>
	private static List<(string Rel, string Texto)> SoProducao(IEnumerable<(string Rel, string Texto)> fontes) =>
		[.. fontes.Where(f => !f.Rel.Contains("Teste", StringComparison.Ordinal)
						   && !f.Rel.Contains("Prova", StringComparison.Ordinal)
						   && !f.Rel.Contains("/Robo", StringComparison.Ordinal))];

	/// <summary>
	/// O CORPO DE UM METODO, do cabecalho ate o proximo membro no mesmo recuo.
	///
	/// E aproximado de proposito e a aproximacao esta declarada: ela para no proximo `\tpublic`,
	/// `\tprivate`, `\tinternal` ou `\t/// &lt;summary&gt;`. Serve pro que esta familia precisa --
	/// perguntar "este consumidor especifico chama o envelope?" -- e a prova de montagem logo abaixo
	/// dela confere que o corpo achado nao veio vazio, senao a resposta seria "sim" por vacuo.
	/// </summary>
	private static string CorpoDoMetodo(string fonte, string assinatura)
	{
		int i = fonte.IndexOf(assinatura, StringComparison.Ordinal);
		if (i < 0) return "";

		int fim = fonte.Length;
		foreach (string marca in new[] { "\n\tpublic ", "\n\tprivate ", "\n\tinternal ", "\n\t/// <summary>" })
		{
			int j = fonte.IndexOf(marca, i + assinatura.Length, StringComparison.Ordinal);
			if (j > 0 && j < fim) fim = j;
		}
		return fonte[i..fim];
	}

	/// <summary>
	/// ============================ A VARREDURA DO FIO ============================
	/// Procura um numero proibido DENTRO dos bytes que sairam no fio, sem saber como ele teria sido
	/// escrito: `double`, `float`, `int32`, `int64` em qualquer deslocamento, e tambem por extenso
	/// (o vazamento mais provavel de todos e alguem interpolar o numero numa string).
	///
	/// **Ela e testada com um pacote envenenado** na familia do sigilo. Uma varredura que nunca acha
	/// nada e indistinguivel de uma varredura que nao varre -- e num pacote de dois bytes ela nao tem
	/// nem janela onde procurar, que e exatamente o caso que parece verde de graca.
	/// ==========================================================================
	/// </summary>
	private static List<string> VarrerOFio(IEnumerable<byte[]> pacotes, IReadOnlyList<(string Nome, double Valor)> proibidos)
	{
		var vazou = new List<string>();
		foreach (byte[] p in pacotes)
		{
			string texto = System.Text.Encoding.UTF8.GetString(p);
			foreach ((string nome, double alvo) in proibidos)
			{
				if (Math.Abs(alvo) < 1e-12) continue;
				double tol = Math.Abs(alvo) * 0.005;

				for (int i = 0; i + 8 <= p.Length; i++)
				{
					double d = BitConverter.ToDouble(p, i);
					if (!double.IsNaN(d) && Math.Abs(d - alvo) <= tol) vazou.Add($"{nome} como double em {i}");
					long l = BitConverter.ToInt64(p, i);
					if (Math.Abs(l - alvo) <= tol) vazou.Add($"{nome} como int64 em {i}");
				}
				for (int i = 0; i + 4 <= p.Length; i++)
				{
					float fl = BitConverter.ToSingle(p, i);
					if (!float.IsNaN(fl) && Math.Abs(fl - alvo) <= tol) vazou.Add($"{nome} como float em {i}");
					int n = BitConverter.ToInt32(p, i);
					if (Math.Abs(n - alvo) <= tol) vazou.Add($"{nome} como int32 em {i}");
				}

				if (alvo >= 1 && texto.Contains(((long)Math.Round(alvo)).ToString(
						System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal))
					vazou.Add($"{nome} escrito por extenso");
			}
		}
		return vazou;
	}

	/// <summary>O disco de um pre-feito na carta estelar, ou nulo quando ele nao esta la.</summary>
	private static PlanetaNoEspaco? DiscoDe(ZoneKey zona)
	{
		foreach (PlanetaNoEspaco p in Espaco.PreFeitos())
			if (p.Nome == zona.Name) return p;
		return null;
	}

	/// <summary>
	/// UM MEMBRO SEM DONO. E nele que o estrago da explosao se mede limpo: `Body.Ferir` escorre uma
	/// fracao do golpe pros membros que tem dono, entao qualquer outro daria `dano x 1,2`. Ver a nota
	/// da familia do commit, que ja pagou por isso.
	/// </summary>
	private static BodyPart? FolhaDe(ServerPlayer pl)
	{
		if (pl.Combate == null) return null;
		foreach (BodyPart p in pl.Combate.Corpo.Partes)
			if (!p.Decepado && !p.Aninhado && p.Dono == null) return p;
		return null;
	}

	/// <summary>Um tiro pronto com pericia escolhida -- o `BaseDano` e o que move o dano cru.</summary>
	private static Projetil TiroDeProva(double bp, double baseDano) => new()
	{
		Tipo = TipoDeProjetil.Blast,
		Bp = bp,
		ModsBase = 1,
		BaseDano = baseDano,
		Nome = "tiro de prova",
	};

	// =====================================================================
	// PROVA 1 -- A FORMULA E UMA SO, POR CONTAGEM
	// =====================================================================
	/// <summary>
	/// ============================ POR QUE CONTAGEM, E NAO COMPARACAO ============================
	/// A bancada de cima ja afirma que `FuriaDoPlaneta(zona)` devolve o mesmo numero que
	/// `MortePlanetaria.Furia(lado, g)`. Isso e verdade -- e continuaria verde se alguem copiasse a
	/// formula pro servidor: **duas implementacoes identicas dao o mesmo numero**, e e exatamente
	/// nesse estado que elas comecam a divergir, porque a proxima pessoa a afinar o balanceamento vai
	/// mexer numa so.
	///
	/// O pedido do dono e "uma formula, dois consumidores", e isso e uma afirmacao sobre a ESTRUTURA
	/// do codigo. Estrutura se prova contando: uma definicao, um envelope, e os dois consumidores
	/// passando por ele.
	/// ==========================================================================================
	/// </summary>
	private ProvaDaMorte AFormulaEUmaSo() => new()
	{
		Nome = "PROVA 1 -- UMA FORMULA, DOIS CONSUMIDORES (contado no fonte)",
		Frase = "dano de quando ele explode = vida do planeta",
		Provas = c =>
		{
			List<(string Rel, string Texto)> casa = FontesDaCasa("Core", "Server", "Client", "Net");
			if (casa.Count < 50) { c("(montagem) o fonte da casa esta no disco", false, $"{casa.Count} arquivos"); return; }
			c("(montagem) o fonte da casa esta no disco", true, $"{casa.Count} arquivos .cs lidos");

			List<(string Rel, string Texto)> producao = SoProducao(casa);

			// ============================ A CONTAGEM E SO EM PRODUCAO -- E A PRIMEIRA RODADA MEDIU A SI MESMA ============================
			// As agulhas desta familia (`double Furia(`, `VidaDoPlaneta`, `bool ComecarDestruicao(`)
			// estao escritas neste proprio arquivo, e nas linhas dos DEFEITOS injetados tambem. Varrer
			// o repo inteiro achava duas definicoes e quatro apelidos, todos dentro da bancada -- seis
			// checagens vermelhas apontando pra ela mesma.
			//
			// Contar so o codigo que roda no jogo do dono e a resposta certa e nao um remendo: "existe
			// uma segunda formula?" e uma pergunta sobre PRODUCAO. Uma segunda copia dentro de uma
			// bancada nao mata planeta nenhum.
			// ==========================================================================================================================

			// ---- 1. UMA definicao, e ela mora no Core ----
			var definicoes = Ocorrencias(producao, "double Furia(");
			c("**UMA UNICA DEFINICAO de `Furia` em todo o repo**", definicoes.Count == 1,
			  string.Join(" | ", definicoes.Select(d => $"{d.Arquivo}:{d.Linha}")));
			c("...e ela mora no Core (o servidor nao tem a conta dele)",
			  definicoes.Count == 1 && definicoes[0].Arquivo.EndsWith("Core/World/PlanetasMortos.cs", StringComparison.Ordinal),
			  definicoes.Count == 1 ? definicoes[0].Arquivo : "");

			// ---- 2. nenhum APELIDO ----
			// Apelido e o primeiro passo pras duas contas: `VidaDoPlaneta(l,g) => Furia(l,g)` parece
			// inocente e sobrevive a qualquer prova de numero, ate o dia em que alguem "corrige" um
			// dos dois lados.
			foreach (string apelido in new[] { "VidaDoPlaneta", "VidaDoMundo", "SaudeDoPlaneta", "PlanetHealth" })
			{
				var achados = Ocorrencias(producao, apelido);
				c($"...e nao ha apelido `{apelido}` (apelido e o primeiro passo pra duas contas)",
				  achados.Count == 0, string.Join(" | ", achados.Select(a => $"{a.Arquivo}:{a.Linha}")));
			}

			// ---- 3. UMA chamada de producao, e ela e o envelope ----
			var chamadas = Ocorrencias(producao, "MortePlanetaria.Furia(");
			GD.Print("    --   as chamadas de producao de `MortePlanetaria.Furia`:");
			foreach ((string arq, int lin, string txt) in chamadas) GD.Print($"         {arq}:{lin}  {CurtoNaProva(txt)}");

			c("**UMA UNICA CHAMADA DE PRODUCAO em todo o jogo** -- todo consumidor passa pelo envelope",
			  chamadas.Count == 1, $"{chamadas.Count} chamadas");
			c("...e essa chamada e o proprio `FuriaDoPlaneta`",
			  chamadas.Count == 1 && chamadas[0].Arquivo.EndsWith("GameServer.Destruicao.cs", StringComparison.Ordinal),
			  chamadas.Count == 1 ? $"{chamadas[0].Arquivo}:{chamadas[0].Linha}" : "");

			// ---- 4. OS DOIS CONSUMIDORES, um por um ----
			string dest = casa.FirstOrDefault(f => f.Rel.EndsWith("GameServer.Destruicao.cs", StringComparison.Ordinal)).Texto ?? "";
			string commit = CorpoDoMetodo(dest, "private void ConsumarDestruicao(");
			string ferir = CorpoDoMetodo(dest, "public bool FerirOMundo(");

			// A MONTAGEM PRIMEIRO: um corpo vazio faria as duas checagens abaixo responderem "nao
			// achei" em vez de "nao chama", e as duas sairiam vermelhas pelo motivo errado.
			c("(montagem) os corpos dos dois consumidores foram achados no fonte",
			  commit.Length > 400 && ferir.Length > 400, $"commit {commit.Length} ch, ferida {ferir.Length} ch");

			c("**CONSUMIDOR 1 (o DANO da explosao) passa pelo envelope**",
			  commit.Contains("FuriaDoPlaneta(zona)", StringComparison.Ordinal));
			c("**CONSUMIDOR 2 (a VIDA do planeta) passa pelo MESMO envelope**",
			  ferir.Contains("FuriaDoPlaneta(zona)", StringComparison.Ordinal));

			// ---- 5. e o envelope de fato devolve a formula do Core (o numero, pra fechar) ----
			(int lado, double g) = MedidasDoPlaneta(Cobaia);
			c("...e o envelope devolve exatamente a formula do Core",
			  Math.Abs(FuriaDoPlaneta(Cobaia) - MortePlanetaria.Furia(lado, g)) < 1e-9,
			  $"{FuriaDoPlaneta(Cobaia):0.###}");
		},
		Defeitos =
		[
			// O DEFEITO CLASSICO: alguem "otimiza" o envelope escrevendo a conta ali dentro. O numero
			// continua certo; a estrutura, nao.
			("a formula foi COPIADA pra dentro do envelope do servidor",
			 _ => _fonteMutanteDaMorte = caminho => File.ReadAllText(caminho).Replace(
				 "return MortePlanetaria.Furia(lado, g);",
				 "return 1500 * (lado / 500.0) * Math.Sqrt(Math.Max(g, 1));")),

			// O OUTRO: o consumidor da explosao ganha conta propria e para de passar pelo envelope.
			("o consumidor da explosao deixou de passar pelo envelope",
			 _ => _fonteMutanteDaMorte = caminho => File.ReadAllText(caminho).Replace(
				 "Agonia.FuriaDaExplosao?.Invoke(zona) ?? FuriaDoPlaneta(zona)",
				 "MortePlanetaria.Furia(lado, gravidade)")),

			// E O APELIDO, que passa em qualquer prova de numero.
			("apareceu um apelido `VidaDoPlaneta` (que sempre passa nas provas de numero)",
			 _ => _fonteMutanteDaMorte = caminho => File.ReadAllText(caminho).Replace(
				 "public double FuriaDoPlaneta(ZoneKey zona)",
				 "public double VidaDoPlaneta(ZoneKey z) => FuriaDoPlaneta(z);\n\tpublic double FuriaDoPlaneta(ZoneKey zona)")),
		],
	};

	// =====================================================================
	// PROVA 2 -- A IGUALDADE, MEDIDA NAS DUAS PONTAS DE PRODUCAO
	// =====================================================================
	/// <summary>
	/// ============================ O PEDIDO DO DONO, EM NUMERO ============================
	/// *"a 'vida' do planeta segue a mesma formula do dano dele quando explode ... entao dano de
	/// quando ele explode = vida do planeta"*.
	///
	/// Esta familia joga a frase inteira: bombardeia um mundo de verdade ate ele cair, **contando
	/// quanta vida ele absorveu**, e depois deixa a explosao dele acontecer e **mede no membro** o
	/// dano que ela aplicou. Os dois numeros tem que ser o mesmo.
	///
	/// Nenhuma das duas metades e calculada aqui: a vida sai de tiros contados um a um no
	/// `AtingirMundoComKi` de producao, e o dano sai da diferenca de vida de um membro depois do
	/// `ConsumarDestruicao` de producao. **Nao ha uma linha de aritmetica minha entre a pergunta e a
	/// resposta** -- so a divisao pelo gap de poder, que e a funcao publica que o jogo inteiro usa.
	/// ==================================================================================
	/// </summary>
	private ProvaDaMorte ADanoIgualAVida() => new()
	{
		Nome = "PROVA 2 -- O DANO DA EXPLOSAO E O MESMO NUMERO QUE ERA A VIDA (medido dos dois lados)",
		Frase = "bombardeia ate cair contando a vida; deixa explodir e mede o dano no membro",
		Provas = c =>
		{
			ZoneKey alvo = Cobaia;                       // Arlia: gravidade 2, o gerado tem escala propria
			(_, double g) = MedidasDoPlaneta(alvo);
			double furia = FuriaDoPlaneta(alvo);
			double portao = MortePlanetaria.BpExigido(g);

			if (DiscoDe(alvo) is not { } disco) { c("(montagem) a cobaia esta na carta estelar", false, alvo.Name); return; }
			c("(montagem) a cobaia esta na carta estelar", true, $"{alvo.Name}, vida {furia:N0}, limiar {portao:N0}");

			// ---- METADE 1: QUANTA VIDA ELE ABSORVEU, contada tiro a tiro no caminho de producao ----
			Projetil tiro = TiroDeProva(portao * 100, baseDano: 1);
			AtingirMundoComKi(disco, tiro, atirador: null);
			double porTiro = FeridaDoMundo(alvo);
			c("(montagem) um tiro de producao de fato fere o mundo", porTiro > 0, $"{porTiro:0.###} por tiro");

			int tiros = 1;
			while (!ZonaCondenada(alvo) && tiros < 100_000) { AtingirMundoComKi(disco, tiro, null); tiros++; }
			double vidaAbsorvida = tiros * porTiro;

			c("**O MUNDO CAIU** depois de absorver a vida inteira, no caminho de producao",
			  ZonaCondenada(alvo), $"{tiros} tiros de {porTiro:0.###}");
			c("**A VIDA QUE ELE ABSORVEU E A FURIA** (medida, e nao comparada com uma segunda conta)",
			  Math.Abs(vidaAbsorvida - furia) <= porTiro,
			  $"absorveu {vidaAbsorvida:0.##}, furia {furia:0.##} (a sobra cabe num tiro de {porTiro:0.##})");

			// ---- METADE 2: QUANTO DANO A EXPLOSAO APLICA, medido no membro ----
			// O corpo fica MUITO acima do limiar deste chao de proposito: assim o gap de poder cai no
			// piso e o dano cabe num membro de 100 de vida, que e o unico jeito de LER o numero. Um
			// corpo fraco levaria a furia inteira, o membro zeraria, e a medicao viraria "morreu".
			ServerPlayer alem = ForjarNaProva(alvo, "AlemDesteMundo", 1);
			AjustarExpressoPara(alem, portao * 100);
			double gap = CombatMath.BpModulus(portao, alem.Ficha.expressedBP);
			c("(montagem) o corpo esta muito acima do limiar -- o gap cai no piso e o dano cabe no membro",
			  gap > 0 && gap <= 0.05, $"gap {gap:0.####}");

			BodyPart? folha = FolhaDe(alem);
			double vidaAntes = folha?.Vida ?? -1;
			c("(montagem) achei um membro sem dono pra medir o estrago limpo", folha != null,
			  folha?.Nome ?? "nenhum");

			for (int s = 0; s < 400 && !ZonaMorta(alvo); s++) TickDaDestruicao(1);
			c("(montagem) a explosao consumou pelo tique de producao", ZonaMorta(alvo));

			double perda = folha != null ? vidaAntes - folha.Vida : 0;
			double furiaMedida = gap > 0 ? perda / gap : 0;

			c("**O DANO QUE A EXPLOSAO APLICOU E EXATAMENTE A FURIA DESTE MUNDO**",
			  Math.Abs(furiaMedida - furia) < 0.01,
			  $"o membro '{folha?.Nome}' perdeu {perda:0.###}; dividido pelo gap {gap:0.####} da "
			  + $"{furiaMedida:0.##}, e a furia e {furia:0.##}");

			c("**E ELE E O MESMO TOTAL DE VIDA QUE O PLANETA TINHA** (o pedido do dono, fechado)",
			  Math.Abs(furiaMedida - vidaAbsorvida) <= porTiro,
			  $"dano da explosao {furiaMedida:0.##} contra vida absorvida {vidaAbsorvida:0.##}");

			// ---- X4, de brinde e no mesmo corpo: quem sobrou subiu, e subiu ONDE o planeta ficava ----
			c("**X4: o corpo terminou no ESPACO**", Espaco.EhEspaco(alem.Zone), alem.Zone.Name);
			c("...no lugar exato onde o planeta ficava (o `PontoDeDecolagem` do disco)",
			  (alem.Pos - Espaco.PontoDeDecolagem(disco)).Length < 1,
			  $"{alem.Pos.X:0},{alem.Pos.Y:0} contra {Espaco.PontoDeDecolagem(disco).X:0},{Espaco.PontoDeDecolagem(disco).Y:0}");
		},
		Defeitos =
		[
			("alguem 'corrigiu' o dano da explosao e ele deixou de ser a vida (dobrou)",
			 s => s.FuriaDaExplosao = z => 2 * FuriaDoPlaneta(z)),

			("voltou o `DanoDoCommit = 99` flat do DM no lugar da furia",
			 s => s.FuriaDaExplosao = _ => 99),
		],
	};

	// =====================================================================
	// PROVA 3 -- A RAMPA, E A FRACAO UNICA
	// =====================================================================
	/// <summary>
	/// ============================ UM VALOR NAO PROVA RAMPA, E UM EFEITO NAO PROVA FRACAO UNICA ============================
	/// *"quanto mais perto ta de explodir, mais intenso esses efeitos ficam"*. Sao duas afirmacoes
	/// dentro de uma frase, e elas falham de jeitos diferentes:
	///
	///   * a rampa pode nao SUBIR -- por isso ela e medida em cinco instantes, e nao num;
	///   * cada efeito pode ter a rampa DELE -- e ai tudo fica verde sozinho e errado junto: o ceu
	///     no auge com o chao no piso, e ninguem ligando uma coisa na outra olhando.
	///
	/// A segunda so se prova quebrando. Com a fracao travada numa constante, os quatro consumidores
	/// medidos pelo `MedirARampa` (ceu, tremor, chao, cratera) tem que cair **juntos**. Se um
	/// sobreviver, ele tem nocao propria de intensidade -- e a fracao unica virou decoracao.
	/// ================================================================================================================
	/// </summary>
	private ProvaDaMorte ARampaEUmaFracaoSo(ServerPlayer pl) => new()
	{
		Nome = "PROVA 3 -- A RAMPA SOBE, E TODO EFEITO LE A MESMA FRACAO",
		Frase = "quanto mais perto ta de explodir, mais intenso esses efeitos ficam",
		Provas = c =>
		{
			// ---- os cinco instantes, impressos ----
			double t = MortePlanetaria.SegundosDeExplosao;
			double[] quando = [t, t * 0.75, t * 0.5, t * 0.25, 0];
			double[] valores = [.. quando.Select(f => MortePlanetaria.Intensidade(FaseDaMorte.Explodindo, 4, f))];

			GD.Print("    --   A RAMPA, MEDIDA EM CINCO INSTANTES DOS 5 MINUTOS:");
			for (int i = 0; i < quando.Length; i++)
				GD.Print($"         faltam {quando[i],6:0} s  ->  intensidade {valores[i]:0.000}"
					   + $"   (ceu {0.45 + 0.55 * valores[i]:0.00}, tremor a cada "
					   + $"{MortePlanetaria.TremorMax + (MortePlanetaria.TremorMin - MortePlanetaria.TremorMax) * valores[i]:0.0} s)");

			bool sobeSempre = true;
			for (int i = 1; i < valores.Length; i++) if (valores[i] <= valores[i - 1]) sobeSempre = false;
			c("**A RAMPA SOBE EM TODOS OS CINCO INSTANTES** (um valor so nao provaria rampa)",
			  sobeSempre, string.Join(" -> ", valores.Select(v => v.ToString("0.000"))));

			// ---- e os QUATRO CONSUMIDORES, no tique de producao ----
			// A familia de cima ja mede isto uma vez; ela e chamada aqui de novo porque e ELA que a
			// injecao do defeito precisa derrubar. Sem re-roda-la, "todos leem a mesma fracao" seria
			// uma frase de comentario.
			MedirARampa(c, pl);
		},
		Defeitos =
		[
			("a rampa virou uma CONSTANTE no piso (o mundo agoniza igual do 1o ao 310o segundo)",
			 s => s.Intensidade = _ => MortePlanetaria.PisoDaAgonia),

			("a rampa virou uma CONSTANTE no auge (tudo no maximo desde o segundo zero)",
			 s => s.Intensidade = _ => 1),

			("a rampa DESCEU (alguem inverteu o `faltam`)",
			 s => s.Intensidade = e => 1 - MortePlanetaria.Intensidade(e)),
		],
	};

	// =====================================================================
	// PROVA 4 -- OS CINCO MINUTOS
	// =====================================================================
	/// <summary>
	/// ============================ AS DUAS METADES DE UM PRAZO ============================
	/// "o planeta explodiu no fim dos 310 s" e meia prova: ela fica verde num sistema que explode no
	/// segundo zero e num que explode no segundo 5. A outra metade e **que ele NAO explodiu antes**,
	/// e ela so se responde perguntando a cada segundo.
	///
	/// O relogio anda pelo `TickDaDestruicao` de PRODUCAO, um segundo por volta. Nao ha atalho que
	/// pule pro commit: se houvesse, a bancada estaria medindo o atalho.
	/// ================================================================================
	/// </summary>
	private ProvaDaMorte OsCincoMinutos() => new()
	{
		Nome = "PROVA 4 -- OS 5 MINUTOS SAO 5 MINUTOS (e a explosao nao acontece antes)",
		Frase = "pedras levitando pelo mapa todo de forma aleatoria por 5 minutos",
		Provas = c =>
		{
			c("o numero do dono bate com o do DM: `sleep(3100)` = 310 s (`Area_Death.dm:129`)",
			  Math.Abs(MortePlanetaria.SegundosDeExplosao - 310) < 1e-9,
			  $"{MortePlanetaria.SegundosDeExplosao:0}");

			c("(montagem) a cobaia esta viva e sem ninguem dentro",
			  !ZonaCondenada(Cobaia) && ZoneList(Cobaia.Hash).Count == 0,
			  $"{ZoneList(Cobaia.Hash).Count} corpos");

			ComecarDestruicao(Cobaia, 1, "prova-dos-5-minutos");
			EstadoDaMorte? e = MorteDaZona(Cobaia);
			c("a explosao nasce com os 310 s inteiros na frente",
			  e != null && Math.Abs(e.Faltam - MortePlanetaria.SegundosDeExplosao) < 1e-9,
			  $"{e?.Faltam:0.#}");

			int caiuNoSegundo = -1;
			FaseDaMorte faseAos309 = FaseDaMorte.Vivo;

			for (int s = 1; s <= 400; s++)
			{
				TickDaDestruicao(1);
				if (s == 309) faseAos309 = MorteDaZona(Cobaia)?.Fase ?? FaseDaMorte.Vivo;
				if (ZonaMorta(Cobaia)) { caiuNoSegundo = s; break; }
			}

			c("**A EXPLOSAO NAO ACONTECE ANTES**: aos 309 s o mundo ainda esta se despedacando",
			  faseAos309 == FaseDaMorte.Explodindo, $"aos 309 s ele estava {faseAos309}");
			c("**E ELA ACONTECE NO SEGUNDO 310**, nem antes nem depois",
			  caiuNoSegundo == (int)MortePlanetaria.SegundosDeExplosao,
			  caiuNoSegundo < 0 ? "nao caiu em 400 s" : $"caiu no segundo {caiuNoSegundo}");
		},
		Defeitos =
		[
			("os cinco minutos viraram um minuto", s => s.SegundosDeExplosao = 60),
			("os cinco minutos viraram quinze", s => s.SegundosDeExplosao = 900),
			("a explosao virou instantanea", s => s.SegundosDeExplosao = 0),
		],
	};

	// =====================================================================
	// PROVA 5 -- O SIGILO DO K5, EM TRES CAMADAS
	// =====================================================================
	/// <summary>
	/// ============================ A REGRA QUE O DONO ESCREVEU COMO PROIBICAO ============================
	/// *"pessoas fracas receberiam um aviso q o ataque dela n fez nada ao planeta (**nao e pra dizer o
	/// bp minimo ou outra coisa**, so dizer q n e forte o suficiente)"*.
	///
	/// Este projeto ja teve um sigilo de BP escrito e **100% orfao** -- a API existia, o corte estava
	/// escrito, e nenhum consumidor o aplicava. A licao ficou: sigilo nao se prova pela intencao de
	/// quem escreveu, e sim pelo que SAI. Entao aqui sao tres camadas, e nenhuma delas le o codigo do
	/// servidor:
	///
	///   1. **A FALA** -- toda linha que o servidor manda pro atacante fraco, varrida DIGITO POR
	///      DIGITO. Varrer por digito e severo de proposito: pega o limiar, o BP, a razao, o quanto
	///      falta, a porcentagem, e pega tambem o que alguem acrescentar daqui a seis meses sem ler
	///      este comentario.
	///   2. **O PACOTE** -- os bytes do `S2C.Mortos` como eles saem do `MandarMortos`, varridos janela
	///      por janela (double, float, int32, int64 e por extenso) atras dos numeros proibidos. E o
	///      caso do mundo FERIDO e o mais forte de todos: o pacote dele tem **zero entradas**, ou
	///      seja a ferida nao viaja porque nao existe campo pra ela viajar.
	///   3. **O FONTE DO CLIENTE** -- ele nao conhece uma unica das grandezas secretas. Nao ha o que
	///      vazar por uma barra de vida de planeta que alguem desenhe amanha, porque o numero nao
	///      atravessa o fio nem por acidente.
	///
	/// **E A VARREDURA E TESTADA**: um pacote envenenado com a ferida dentro tem que ser pego. Sem
	/// isso, a camada 2 estaria afirmando "nao achei" a partir de um pacote de dois bytes, onde nao ha
	/// nem janela onde procurar -- verde de graca, que e o pior tipo de verde.
	/// ==============================================================================================
	/// </summary>
	private ProvaDaMorte OSigiloDoK5(ServerPlayer pl) => new()
	{
		Nome = "PROVA 5 -- O SIGILO DO K5, EM TRES CAMADAS (a fala, o pacote e o fonte do cliente)",
		Frase = "nao e pra dizer o bp minimo ou outra coisa, so dizer q n e forte o suficiente",
		Provas = c =>
		{
			ZoneKey alvo = Cobaia;
			(_, double g) = MedidasDoPlaneta(alvo);
			double furia = FuriaDoPlaneta(alvo);
			double portao = MortePlanetaria.BpExigido(g);
			if (DiscoDe(alvo) is not { } disco) { c("(montagem) a cobaia esta na carta estelar", false); return; }

			// =============================================================
			// CAMADA 1 -- A FALA
			// =============================================================
			List<string>? guarda = EscutaDeAvisos;
			var ditas = new List<string>();
			EscutaDeAvisos = ditas;
			try
			{
				// Cinco tiros do fraco, e a mordaca limpa entre eles pra que TODAS as cinco linhas
				// sejam colhidas. Medir uma linha so deixaria de fora a segunda frase do sistema.
				for (int i = 0; i < 5; i++)
				{
					_faleiDoMundo.Clear();
					AtingirMundoComKi(disco, TiroDeProva(portao * 0.5, 1), pl);
				}

				c("**o fraco E avisado** (o sistema fala, e nao apenas ignora)",
				  ditas.Any(t => t.Contains("forte", StringComparison.OrdinalIgnoreCase)),
				  $"{ditas.Count} linhas");
				c("...e o mundo nao sentiu nada", FeridaDoMundo(alvo) == 0, $"{FeridaDoMundo(alvo):0.###}");

				var comDigito = ditas.Where(t => t.Any(char.IsDigit)).ToList();
				c("**CAMADA 1 (a fala): NENHUMA linha que chega ao fraco carrega um digito**",
				  comDigito.Count == 0,
				  comDigito.Count == 0 ? string.Join(" | ", ditas.Distinct()) : $"vazou: {string.Join(" | ", comDigito)}");

				// E a outra metade da fala: o que ele ouve DEPOIS de ficar forte tambem nao tem numero.
				ditas.Clear();
				_faleiDoMundo.Clear();
				AtingirMundoComKi(disco, TiroDeProva(portao * 100, 1), pl);
				c("...e o retorno de quem CONSEGUE ferir tambem nao tem numero",
				  !ditas.Any(t => t.Any(char.IsDigit)), string.Join(" | ", ditas));
			}
			finally { EscutaDeAvisos = guarda; }

			// =============================================================
			// CAMADA 2 -- O PACOTE
			// =============================================================
			double ferida = FeridaDoMundo(alvo);
			c("(montagem) o mundo esta FERIDO e ainda nao condenado", ferida > 0 && !ZonaCondenada(alvo),
			  $"ferida {ferida:0.###}");

			(string, double)[] proibidos =
			[
				("a ferida", ferida), ("a vida do mundo", furia), ("o limiar de BP", portao),
				("a ancora de tiros", MortePlanetaria.TirosNoLimiar),
				("a ponte de escala", MortePlanetaria.MundoPorPontoDeKi),
			];

			var pacotesFerido = new List<byte[]>();
			EscutaDeMortos = pacotesFerido;
			MandarMortosPraTodos();
			EscutaDeMortos = null;

			c("(montagem) o pacote de estado de morte saiu no fio", pacotesFerido.Count > 0,
			  $"{pacotesFerido.Count} pacote(s), {pacotesFerido.FirstOrDefault()?.Length ?? 0} bytes");
			c("**CAMADA 2a: o pacote de um mundo FERIDO tem ZERO entradas** -- a ferida nao viaja "
			  + "porque nao ha campo pra ela viajar",
			  pacotesFerido.Count > 0 && pacotesFerido.All(p => p.Length <= 2 && p[^1] == 0),
			  string.Join(" | ", pacotesFerido.Select(p => $"{p.Length}B")));

			// E AGORA COM O MUNDO CONDENADO, que e quando o pacote de fato carrega alguma coisa. Este
			// e o caso em que uma varredura preguicosa acharia o vazamento -- e ele tem que estar limpo.
			ComecarDestruicao(alvo, 1e9, "prova-do-sigilo");
			var pacotesMorto = new List<byte[]>();
			EscutaDeMortos = pacotesMorto;
			MandarMortosPraTodos();
			EscutaDeMortos = null;

			c("(montagem) agora o pacote carrega a condenacao (que e publica: todo mundo esta vendo)",
			  pacotesMorto.Count > 0 && pacotesMorto[0].Length > 10,
			  $"{pacotesMorto.FirstOrDefault()?.Length ?? 0} bytes");

			List<string> vazou = VarrerOFio(pacotesFerido.Concat(pacotesMorto), proibidos);
			c("**CAMADA 2b: nenhum numero proibido aparece nos BYTES que saem** (double, float, int e "
			  + "por extenso, em qualquer deslocamento)",
			  vazou.Count == 0, vazou.Count == 0 ? "" : string.Join(" | ", vazou.Distinct().Take(5)));

			// E A VARREDURA SABE ACHAR? Um pacote envenenado com a ferida dentro. **Sem esta linha a
			// camada 2 estaria dizendo "nao achei" a partir de um pacote de dois bytes, onde nao ha
			// nem janela onde procurar** -- verde de graca, que e o pior tipo de verde.
			byte[] baseDoVeneno = pacotesMorto.FirstOrDefault() ?? [1, 1];
			byte[] envenenado = [.. baseDoVeneno, .. BitConverter.GetBytes(ferida)];
			c("...e a varredura SABE achar: um pacote envenenado com a ferida e pego",
			  VarrerOFio([envenenado], proibidos).Count > 0);
			c("...e ela acha tambem quando o numero vai por EXTENSO (o vazamento mais provavel)",
			  VarrerOFio([System.Text.Encoding.UTF8.GetBytes($"faltam {portao:0} de BP")], proibidos).Count > 0);

			// =============================================================
			// CAMADA 3 -- O FONTE DO CLIENTE
			// =============================================================
			List<(string Rel, string Texto)> cliente = FontesDaCasa("Client", "Net");
			c("(montagem) o fonte do cliente esta no disco", cliente.Count > 10, $"{cliente.Count} arquivos");

			string[] segredos =
			[
				"BpExigido", "TirosNoLimiar", "MundoPorPontoDeKi", "FeridaDoMundo", "FeridaDeMundo",
				"DanoNoMundo", "ForteOBastantePraFerirOMundo", "DanoNoCorpo", "MortePlanetaria.Furia",
				"FuriaBase", "FuriaDoPlaneta",
			];
			var conhecidas = segredos.Where(s => Ocorrencias(cliente, s).Count > 0).ToList();
			c("**CAMADA 3: o cliente nao conhece NENHUMA das grandezas secretas** -- nao ha o que "
			  + "vazar por uma barra de vida que alguem desenhe amanha",
			  conhecidas.Count == 0, conhecidas.Count == 0
				  ? $"{segredos.Length} nomes conferidos em {cliente.Count} arquivos"
				  : $"conhece: {string.Join(", ", conhecidas)}");

			// A METADE QUE FECHA: o cliente conhece o que E publico. Sem ela, um cliente que nao
			// conhecesse NADA da agonia (porque alguem apagou o sistema) passaria verde aqui.
			c("...mas ele conhece a AGONIA, que e publica (senao a rampa na tela nao existiria)",
			  Ocorrencias(cliente, "MortePlanetaria.Intensidade").Count > 0);
		},
		Defeitos =
		[
			("o aviso resolveu ajudar e disse o limiar",
			 s => s.TextoDoFraco = nome => $"você precisa de {MortePlanetaria.BpExigido(2):N0} de BP "
										 + $"expresso para ferir {nome}."),

			("o aviso disse quanto FALTA (uma barra de vida escrita por extenso)",
			 s => s.TextoDoFraco = nome => $"{nome} está a 37% da queda, mas seu golpe não bastou."),

			("o aviso vazou so um algarismo, no meio de uma frase inocente",
			 s => s.TextoDoFraco = nome => $"{nome} mal sentiu: faltam 3 vezes mais poder."),
		],
	};

	// =====================================================================
	// PROVA 6 -- A TABELA DO K4, MEDIDA
	// =====================================================================
	/// <summary>
	/// ============================ CONTADA, E NAO DIVIDIDA ============================
	/// A bancada de cima imprime uma tabela de `vida / dano`. Ela esta certa e nao e a mesma coisa:
	/// uma divisao no papel continuaria imprimindo numeros bonitos num sistema em que o tiro **nao
	/// chega ao planeta**, em que a ferida nao acumula, ou em que ela cicatriza mais rapido do que o
	/// atacante bate. Aqui cada celula e um LACO: tiros de producao, um a um, ate o mundo cair.
	///
	/// O que a tabela responde e o pedido inteiro do dono numa figura so: *"pessoas MUITO fortes
	/// poderiam zerar a vida do planeta rapidamente, mas pessoas mais fracas demorariam mt mais
	/// tempo"* -- e a coluna do "nao fere" e o K5 aparecendo na mesma tabela.
	/// ==============================================================================
	/// </summary>
	private ProvaDaMorte ATabelaMedidaDoK4() => new()
	{
		Nome = "PROVA 6 -- QUANTOS TIROS, CONTADOS UM A UM NO CAMINHO DE PRODUCAO",
		Frase = "pessoas MUITO fortes zerariam rapido, mas pessoas mais fracas demorariam mt mais tempo",
		Provas = c =>
		{
			const int Teto = 60_000;
			(string Nome, double Bruto)[] pericias = [("cru", 1), ("veterano", 250), ("mestre", 2250)];
			(string Rotulo, double Fator)[] faixas =
				[("abaixo do limiar", 0.5), ("no limiar", 1), ("100x o limiar", 100), ("10.000x", 10_000)];

			// A TABELA E MONTADA E SO DEPOIS IMPRESSA. Cada celula derruba um mundo de verdade, e a
			// morte de um mundo cospe seis linhas de anuncio no console -- imprimir linha a linha
			// deixava a tabela picada em pedacos de tres numeros no meio do log. O trabalho e o mesmo;
			// o que muda e alguem conseguir LER o resultado.
			var tabela = new List<string> { "    --   K4 MEDIDO: tiros de producao ate o mundo cair (Basic Blast, ~3,3 tiros/s)" };
			var medidas = new Dictionary<string, int>();

			foreach (string nome in new[] { "Earth", "Vegeta", "Icer" })
			{
				var z = ZoneKey.Premade(nome);
				if (DiscoDe(z) is not { } disco) continue;
				(_, double g) = MedidasDoPlaneta(z);
				double vida = FuriaDoPlaneta(z);
				double portao = MortePlanetaria.BpExigido(g);

				tabela.Add($"         {nome} -- vida {vida:N0}, limiar de BP expresso {portao:N0}   "
						 + $"| pericia:      CRU        VETERANO         MESTRE");
				foreach ((string rotulo, double fator) in faixas)
				{
					string linha = $"           BP {rotulo,-16}";
					foreach ((string p, double baseDano) in pericias)
					{
						_feridasDeMundo.Clear();
						_mortos.Limpar();

						Projetil tiro = TiroDeProva(portao * fator, baseDano);
						int n = 0;
						AtingirMundoComKi(disco, tiro, null);
						n++;

						if (FeridaDoMundo(z) <= 0 && !ZonaCondenada(z))
						{
							linha += "     -- nao fere --";
							medidas[$"{nome}|{rotulo}|{p}"] = -1;
							continue;
						}

						while (!ZonaCondenada(z) && n < Teto) { AtingirMundoComKi(disco, tiro, null); n++; }
						bool caiu = ZonaCondenada(z);
						medidas[$"{nome}|{rotulo}|{p}"] = caiu ? n : -2;
						string quantos = caiu ? n.ToString("N0") : $">{Teto:N0}";
						linha += $"   {quantos,10} tiros";

						_mortos.Limpar();
						_feridasDeMundo.Clear();
						ForcarClima(z, TipoDeClima.Limpo, 0);
					}
					tabela.Add(linha);
				}
			}

			foreach (string l in tabela) GD.Print(l);

			// AS TRES AFIRMACOES QUE A TABELA TEM QUE SUSTENTAR, e nenhuma delas e "os numeros sairam".
			c("**K5 na tabela: abaixo do limiar NENHUMA pericia fere, em nenhum dos tres mundos**",
			  pericias.All(p => new[] { "Earth", "Vegeta", "Icer" }.All(
				  m => medidas.GetValueOrDefault($"{m}|abaixo do limiar|{p.Nome}", 0) == -1)),
			  string.Join(" ", medidas.Where(kv => kv.Key.Contains("abaixo")).Select(kv => $"{kv.Key}={kv.Value}")));

			c("**K4 (o fraco): no limiar, com o tiro mais cru, sao milhares de tiros** -- a rota de "
			  + "orbita tem que ser mais cara que os 30 s do verb",
			  medidas.GetValueOrDefault("Earth|no limiar|cru", 0) >= 1000,
			  $"{medidas.GetValueOrDefault("Earth|no limiar|cru", 0):N0} tiros na Terra");

			c("**K4 (o forte): dez mil vezes o limiar derruba o mesmo mundo num tiro so**",
			  medidas.GetValueOrDefault("Earth|10.000x|mestre", 0) == 1
			  && medidas.GetValueOrDefault("Icer|10.000x|mestre", 0) == 1,
			  $"Terra {medidas.GetValueOrDefault("Earth|10.000x|mestre", 0)}, "
			  + $"Icer {medidas.GetValueOrDefault("Icer|10.000x|mestre", 0)}");

			c("...e mais PERICIA custa menos tiros com o MESMO poder (os dois eixos sao independentes)",
			  medidas.GetValueOrDefault("Earth|no limiar|mestre", int.MaxValue)
			  < medidas.GetValueOrDefault("Earth|no limiar|cru", 0),
			  $"cru {medidas.GetValueOrDefault("Earth|no limiar|cru", 0):N0} contra "
			  + $"mestre {medidas.GetValueOrDefault("Earth|no limiar|mestre", 0):N0}");

			c("...e um mundo mais pesado custa mais tiros que a Terra (a mesma pericia, o mesmo gap)",
			  medidas.GetValueOrDefault("Icer|100x o limiar|cru", 0)
			  > medidas.GetValueOrDefault("Earth|100x o limiar|cru", 0),
			  $"Terra {medidas.GetValueOrDefault("Earth|100x o limiar|cru", 0):N0} contra "
			  + $"Icer {medidas.GetValueOrDefault("Icer|100x o limiar|cru", 0):N0}");
		},
		Defeitos =
		[
			("o portao de forca foi REMOVIDO (o fraco passou a ferir o mundo)",
			 s =>
			 {
				 s.ForteOBastante = (_, _) => true;
				 s.DanoNoMundo = (bruto, g, bp) => Math.Max(bruto, 0)
					 * CombatMath.BpModulus(bp, MortePlanetaria.BpExigido(g))
					 * MortePlanetaria.MundoPorPontoDeKi;
			 }),

			("o gap de poder saiu da conta (todo mundo bate igual, forte ou fraco)",
			 s => s.DanoNoMundo = (bruto, g, bp) =>
				 MortePlanetaria.ForteOBastantePraFerirOMundo(bp, g)
					 ? Math.Max(bruto, 0) * MortePlanetaria.MundoPorPontoDeKi : 0),
		],
	};

	// =====================================================================
	// PROVA 7 -- A MESMA PORTA DO K6
	// =====================================================================
	/// <summary>
	/// ============================ "IGUAL E COM PLANET DESTROY" SE PROVA CONTANDO ============================
	/// *"ao zerar a vida do planeta, ia comecar a contagem dos 5 minutos igual e com planet destroy"*.
	///
	/// A bancada de cima confere o RESULTADO: depois do ultimo tiro, o registro tem fase, estagio,
	/// prazo, relogio de tremor e ceu de destruicao -- tudo o que so o `ComecarDestruicao` escreve.
	/// Isso e forte, e ainda assim ficaria verde se alguem escrevesse um `ComecarDestruicaoPorKi` que
	/// copiasse aquelas seis linhas: no dia seguinte os dois caminhos divergem e ninguem ve.
	///
	/// Entao aqui se conta: uma definicao, e os seis caminhos de producao que a chamam listados por
	/// nome. A prova e sobre a AUSENCIA de uma segunda porta.
	/// ====================================================================================================
	/// </summary>
	private ProvaDaMorte AMesmaPortaDoK6() => new()
	{
		Nome = "PROVA 7 -- ZERAR A VIDA ENTRA PELA MESMA PORTA (contagem de chamadores)",
		Frase = "ao zerar a vida do planeta, ia comecar a contagem dos 5 minutos igual e com planet destroy",
		Provas = c =>
		{
			List<(string Rel, string Texto)> casa = FontesDaCasa("Core", "Server", "Client", "Net");
			List<(string Rel, string Texto)> producao = SoProducao(casa);

			// So em PRODUCAO, pelo mesmo motivo da PROVA 1: as agulhas e os defeitos injetados moram
			// neste arquivo, e varrer o repo inteiro faria a bancada acusar a si mesma.
			var definicoes = Ocorrencias(producao, "bool ComecarDestruicao(");
			c("**UMA UNICA DEFINICAO de `ComecarDestruicao`**", definicoes.Count == 1,
			  string.Join(" | ", definicoes.Select(d => $"{d.Arquivo}:{d.Linha}")));

			// E NENHUMA IRMA: `ComecarDestruicaoPorKi`, `DestruirPorBombardeio`... uma segunda porta
			// com nome parecido e o jeito mais comum de este sistema se partir em dois.
			var irmas = Ocorrencias(producao, "ComecarDestruicao")
				.Where(o => o.Texto.Contains("ComecarDestruicaoP", StringComparison.Ordinal)
						 || o.Texto.Contains("ComecarDestruicaoD", StringComparison.Ordinal)).ToList();
			c("...e nao ha uma segunda porta de nome parecido", irmas.Count == 0,
			  string.Join(" | ", irmas.Select(i => $"{i.Arquivo}:{i.Linha}")));

			var chamadores = Ocorrencias(producao, "ComecarDestruicao(")
				.Where(o => !o.Texto.Contains("bool ComecarDestruicao(", StringComparison.Ordinal)).ToList();

			GD.Print("    --   OS CAMINHOS DE PRODUCAO QUE MATAM UM MUNDO (todos pela mesma porta):");
			foreach ((string arq, int lin, string txt) in chamadores) GD.Print($"         {arq}:{lin}  {CurtoNaProva(txt)}");

			string dest = casa.FirstOrDefault(f => f.Rel.EndsWith("GameServer.Destruicao.cs", StringComparison.Ordinal)).Texto ?? "";
			string ferir = CorpoDoMetodo(dest, "public bool FerirOMundo(");
			string carga = CorpoDoMetodo(dest, "private void TickDaCargaDeDestruicao(");

			c("(montagem) os corpos do K e do verb foram achados no fonte",
			  ferir.Length > 400 && carga.Length > 400, $"K {ferir.Length} ch, verb {carga.Length} ch");

			c("**O K6 (zerar a vida com ki) chama a porta**",
			  ferir.Contains("ComecarDestruicao(zona, bpDoAlgoz", StringComparison.Ordinal));
			c("**O PLANET DESTROY (a carga de 30 s) chama A MESMA porta**",
			  carga.Contains("ComecarDestruicao(zona, bp,", StringComparison.Ordinal));

			c("...e os outros quatro caminhos tambem entram por ela (pavio lento, saga, Final "
			  + "Explosion e o verb de admin)",
			  chamadores.Count >= 6, $"{chamadores.Count} chamadores de producao");

			// E O RESULTADO, que a contagem sozinha nao da: a porta escreve o que so ela escreve.
			ComecarDestruicao(Cobaia, 4242, "prova-da-porta");
			EstadoDaMorte? e = MorteDaZona(Cobaia);
			c("...e passar por ela escreve o estado inteiro (fase, estagio, prazo e relogio de tremor)",
			  e is { Fase: FaseDaMorte.Explodindo }
			  && e.Estagio == MortePlanetaria.UltimoEstagio + 1
			  && Math.Abs(e.Faltam - MortePlanetaria.SegundosDeExplosao) < 1e-9
			  && _proximoTremor.ContainsKey(e.Chave),
			  $"{e?.Fase} estagio {e?.Estagio} faltam {e?.Faltam:0}");
		},
		Defeitos =
		[
			("o K ganhou uma porta propria (`ComecarDestruicaoPorKi`)",
			 _ => _fonteMutanteDaMorte = caminho => File.ReadAllText(caminho).Replace(
				 "ComecarDestruicao(zona, bpDoAlgoz, motivo, algoz?.Id ?? 0);",
				 "ComecarDestruicaoPorKi(zona, bpDoAlgoz, motivo, algoz?.Id ?? 0);")),

			("o verb Planet Destroy deixou de passar pela porta",
			 _ => _fonteMutanteDaMorte = caminho => File.ReadAllText(caminho).Replace(
				 "if (!ComecarDestruicao(zona, bp,", "if (!DestruirDireto(zona, bp,")),
		],
	};

	// =====================================================================
	// PROVA 8 -- AS CONSEQUENCIAS NO RESTO DO JOGO
	// =====================================================================
	/// <summary>
	/// ============================ ISTO TEM QUE SER MEDIDO E RELATADO, NAO DESCOBERTO NO BETA ============================
	/// Destruir a Terra nao e um efeito local: ela e o berco de dez das vinte e quatro racas e o RECUO
	/// de todas as outras. A pergunta "quem passa a nascer onde?" tem uma resposta exata, e ela nao
	/// esta escrita em lugar nenhum -- ela e uma consequencia da POSICAO numa lista
	/// (`Espaco.PreFeitos()`), e nao de uma regra. Por isso ela e medida raca a raca aqui, com a
	/// cascata inteira, e vai pro relatorio do dono.
	///
	/// **E a metade que incomoda tambem sai**: tres sistemas do jogo nao perguntam se o planeta
	/// morreu. Eles nao viram FALHA aqui de proposito -- nao foram pedidos e nao estao quebrados --,
	/// mas saem como ACHADO, porque o modo de falha deles e silencioso e o dono precisa decidir.
	///
	/// **ERAM QUATRO.** O quarto eram as ESFERAS, e ele era o grave: o Porunga ficava ancorado num
	/// planeta em que ninguem podia pisar, mantido vivo a forca pelo zelador, de segundo em segundo. O
	/// dono decidiu (*"porunga morre em namek quando namek explode, so voltando quando o planeta e
	/// restaurado"*) e o achado virou o bloco (e) -- duas checagens que sabem ficar vermelhas, em vez
	/// de uma frase de relatorio que nunca fica.
	/// ================================================================================================================
	/// </summary>
	private ProvaDaMorte AsConsequenciasNoResto() => new()
	{
		Nome = "PROVA 8 -- O QUE A MORTE DE UM MUNDO FAZ COM O RESTO DO JOGO",
		Frase = "se destruir a Terra faz 10 racas nascerem noutro lugar, isso tem que ser MEDIDO",
		Provas = c =>
		{
			var achados = new List<string>();
			void Achado(string texto) { achados.Add(texto); GD.Print($"    --   ACHADO: {texto}"); }

			// =============================================================
			// (a) O BERCO -- as 24 racas, uma a uma, com a cascata
			// =============================================================
			List<string> racas = ConjuntoDeRacas();
			c("(montagem) o conjunto de racas veio das duas fontes", racas.Count >= 20, $"{racas.Count} racas");

			Dictionary<string, string> Onde()
			{
				var mapa = new Dictionary<string, string>(StringComparer.Ordinal);
				foreach (string r in racas)
				{
					string natal = Bercos.PlanetaNatal(r);
					var b = new Berco { Planeta = natal, PreFeito = true, Natal = natal };
					mapa[r] = DestinoDoBerco(b).Zona.Name;
				}
				return mapa;
			}

			Dictionary<string, string> vivo = Onde();
			int naTerraVivo = vivo.Count(kv => kv.Value == "Earth");
			c("(montagem) com tudo vivo, cada raca nasce no berco dela",
			  vivo.All(kv => kv.Value == Bercos.PlanetaNatal(kv.Key)),
			  $"{naTerraVivo} das {racas.Count} nascem na Terra");

			GD.Print("    --   O BERCO, MEDIDO RACA A RACA, NA CASCATA DE MORTES:");
			string[] cascata = ["Earth", "Namek", "Vegeta"];
			var mortos = new List<string>();
			foreach (string vitima in cascata)
			{
				ComecarDestruicao(ZoneKey.Premade(vitima), 1, "prova-consequencias");
				MorteDaZona(ZoneKey.Premade(vitima))!.Fase = FaseDaMorte.Destruido;
				mortos.Add(vitima);

				Dictionary<string, string> agora = Onde();
				var mudaram = racas.Where(r => agora[r] != vivo[r]).ToList();
				var porDestino = mudaram.GroupBy(r => agora[r]).OrderByDescending(gp => gp.Count()).ToList();

				GD.Print($"         com {string.Join(" + ", mortos)} morto(s): {mudaram.Count} das "
					   + $"{racas.Count} racas mudam de berco");
				foreach (var gp in porDestino)
					GD.Print($"           -> {gp.Count(),2} para {gp.Key,-12} ({string.Join(", ", gp.OrderBy(x => x, StringComparer.Ordinal))})");

				c($"**NINGUEM NASCE NO CADAVER** ({string.Join("+", mortos)} morto(s))",
				  !agora.Values.Any(v => mortos.Contains(v)),
				  string.Join(",", agora.Values.Distinct()));
				c($"...e todo destino de recuo esta VIVO ({string.Join("+", mortos)})",
				  agora.Values.All(v => !ZonaMorta(ZoneKey.Premade(v))));

				if (vitima == "Earth")
					Achado($"a Terra sozinha move {mudaram.Count} das {racas.Count} racas "
						 + $"para {string.Join("/", porDestino.Select(gp => gp.Key))} -- e o destino e a POSICAO "
						 + "numa lista (`Espaco.PreFeitos`), nao uma regra por raca");
			}

			// A OUTRA METADE: restaurar devolve todo mundo. Sem ela, "ninguem nasce no cadaver"
			// ficaria verde num berco que parou de funcionar por inteiro.
			foreach (string vitima in cascata) RessuscitarPlaneta(ZoneKey.Premade(vitima));
			Dictionary<string, string> depois = Onde();
			c("**E RESTAURAR OS TRES DEVOLVE TODA RACA AO BERCO DELA** (as duas metades)",
			  racas.All(r => depois[r] == vivo[r]),
			  $"{racas.Count(r => depois[r] != vivo[r])} racas fora do lugar");

			// =============================================================
			// (b) OS CONSUMIDORES QUE PERGUNTAM -- o censo, no fonte
			// =============================================================
			List<(string Rel, string Texto)> servidor = SoProducao(FontesDaCasa("Server", "Core"));
			var censo = Ocorrencias(servidor, "ZonaMorta(").Concat(Ocorrencias(servidor, "ZonaCondenada("))
				.Concat(Ocorrencias(servidor, "PlanetaMorto("))
				.GroupBy(o => o.Arquivo).OrderBy(gp => gp.Key, StringComparer.Ordinal).ToList();

			GD.Print("    --   QUEM PERGUNTA SE O PLANETA MORREU (censo no fonte de producao):");
			foreach (var gp in censo) GD.Print($"         {gp.Key}  ({gp.Count()}x)");

			// `GameServer.Esferas.cs` ENTROU NESTA LISTA, e ele veio da lista de baixo: ele era o
			// ACHADO GRAVE desta prova (*"as ESFERAS ancoradas -- o set ETERNO de Namek e o caso
			// grave"*) e virou regra. Ver o bloco (e) la embaixo, que mede as duas metades.
			string[] esperados = ["GameServer.Berco.cs", "GameServer.Conquista.cs", "GameServer.Invasao.cs",
								  "GameServer.Povoamento.cs", "GameServer.Espaco.cs", "GameServer.Destruicao.cs",
								  "GameServer.Esferas.cs"];
			foreach (string arq in esperados)
				c($"o consumidor `{arq}` esta ligado",
				  censo.Any(gp => gp.Key.EndsWith(arq, StringComparison.Ordinal)));

			// =============================================================
			// (c) O DOMINIO -- o unico sistema social com as duas metades ligadas
			// =============================================================
			var dominioNaTerra = new Dominio { PreFeito = true, Planeta = "Earth" };
			c("(a outra metade) com a Terra viva o dominio dela sobrevive", !PlanetaDoDominioMorreu(dominioNaTerra));

			ComecarDestruicao(ZoneKey.Premade("Earth"), 1, "prova-dominio");
			MorteDaZona(ZoneKey.Premade("Earth"))!.Fase = FaseDaMorte.Destruido;
			c("**O DOMINIO PERDE O TERRITORIO quando o planeta morre**", PlanetaDoDominioMorreu(dominioNaTerra));

			// =============================================================
			// (d) OS QUE **NAO** PERGUNTAM -- medidos, e relatados como achado
			// =============================================================
			foreach ((string arquivo, string sistema) in new[]
			{
				("GameServer.Ranks.cs", "o TRONO e os CARGOS (o Rei de um planeta destruido continua rei)"),
				("GameServer.Tech.cs", "as OBRAS do `mundo.json` (casas, bancadas e lapides ficam de pe)"),
				("GameServer.Reputacao.cs", "a REPUTACAO por planeta (vira livro de um lugar que nao existe)"),
			})
			{
				var f = servidor.Where(x => x.Rel.EndsWith(arquivo, StringComparison.Ordinal)).ToList();
				if (f.Count == 0) continue;
				int perguntas = Ocorrencias(f, "ZonaMorta(").Count + Ocorrencias(f, "ZonaCondenada(").Count;
				if (perguntas == 0) Achado($"`{arquivo}` NAO pergunta se o planeta morreu -- {sistema}");
			}

			// =============================================================
			// (e) AS ESFERAS ANCORADAS -- o achado grave que virou REGRA
			// =============================================================
			// ============================ ESTE BLOCO ERA UM `Achado`, E A INVERSAO E O PONTO ============================
			// Ele dizia, palavra por palavra: *"o set ETERNO (Porunga) continua ancorado em 'Namek', e
			// `ZonaMorta` diz True sobre esse lugar -- o pouso vindo de orbita e recusado e
			// `z02_Namek.passagens` e vazio, entao ele fica inalcancavel"*. Era uma frase pro dono
			// decidir, e ele decidiu: *"sim porunga morre em namek quando namek explode, so voltando
			// quando o planeta e restaurado pelas esferas de outro lugar"*.
			//
			// Virando regra, ele deixa de ser `Achado` (que nunca fica vermelho) e vira `c` (que fica).
			// Manter as duas formas seria uma prova que RELATA o que a outra AFIRMA -- e o dia em que a
			// regra quebrasse, o relatorio continuaria dizendo que estava tudo certo.
			//
			// **A MORTE AQUI ENTRA PELO TIQUE** (`TickDasEsferas`, o funil de producao) e nao pelo
			// commit: esta prova carimba a fase a mao pra nao matar os habitantes de Namek a cada
			// rodada. Quem mede o commit de ponta a ponta e a `--porungateste`, que existe pra isso.
			// ======================================================================================================
			SetDeEsferas? eterno = _sets.FirstOrDefault(s => s.Eterno);
			c("(montagem) o set ETERNO esta de pe em Namek antes do tiro",
			  eterno != null && string.Equals(eterno.ZonaNome, "Namek", StringComparison.OrdinalIgnoreCase),
			  eterno?.ZonaNome ?? "(nao existe)");

			ComecarDestruicao(ZoneKey.Premade("Namek"), 1, "prova-esferas");
			MorteDaZona(ZoneKey.Premade("Namek"))!.Fase = FaseDaMorte.Destruido;
			TickDasEsferas();

			c("**O SET ETERNO (Porunga) MORRE COM NAMEK**", !_sets.Any(s => s.Eterno),
			  "o Porunga continua ancorado num planeta em que ninguem pode pisar");
			c("...e as sete dele somem junto",
			  eterno == null || !_esferas.Any(x => x.Set == eterno.Id),
			  eterno == null ? "-" : $"{_esferas.Count(x => x.Set == eterno.Id)} esferas orfas");

			// O QUE **NAO** MORRE COM O PLANETA, contado com o cadaver ainda no chao (depois da
			// restauracao a frase perderia o sentido).
			int obras = _noChao.Count(o => o.Zona.Equals(ZoneKey.Premade("Namek")));
			if (obras > 0) Achado($"{obras} obra(s) do `mundo.json` continuam de pe em Namek depois dela morrer");

			// A OUTRA METADE, e ela e o pedido do dono inteiro: sem esta linha, "o Porunga morre"
			// ficaria verde num sistema que simplesmente tirou o Porunga do jogo pra sempre.
			RessuscitarPlaneta(ZoneKey.Premade("Namek"));
			c("**...E VOLTA QUANDO O PLANETA E RESTAURADO** (as duas metades)",
			  _sets.Any(s => s.Eterno && string.Equals(s.ZonaNome, "Namek", StringComparison.OrdinalIgnoreCase)),
			  "restaurar Namek nao trouxe o Porunga de volta");

			c("(fecho) esta familia mediu as consequencias e nao inventou nenhuma",
			  achados.Count > 0, $"{achados.Count} achado(s) pro relatorio");
		},
		Defeitos =
		[
			// O DEFEITO CLASSICO DESTE REPO, e ele ja aconteceu: a pergunta e renomeada e **parte** dos
			// consumidores acompanha. O censo e justamente quem pega isso -- o jogo continua rodando,
			// o berco continua desviando, e um dia alguem descobre que a invasao nao desviava.
			("a pergunta `ZonaMorta` foi renomeada e os consumidores ficaram pra tras",
			 _ => _fonteMutanteDaMorte = caminho => File.ReadAllText(caminho)
				 .Replace("ZonaMorta(", "PerguntaVelhaDeMorte(")),

			("a pergunta `ZonaCondenada` foi renomeada e os consumidores ficaram pra tras",
			 _ => _fonteMutanteDaMorte = caminho => File.ReadAllText(caminho)
				 .Replace("ZonaCondenada(", "PerguntaVelhaDeCondenacao(")),
		],
	};

	// =====================================================================
	// PROVA 9 -- AS BORDAS DO K QUE FALTAVAM
	// =====================================================================
	/// <summary>
	/// A bancada de cima ja mede quatro bordas do K: dois atacantes somam, o atacante que sumiu, o
	/// mundo ja CONDENADO e o mundo DESTRUIDO. Falta a quinta, e ela e diferente das outras porque o
	/// mundo esta **vivo e agonizando ao mesmo tempo**: o pavio lento de 20 minutos.
	///
	/// O modo de falha e especifico: `ZonaCondenada` responde "sim" durante o pavio, entao o tiro tem
	/// que ser recusado -- mas se alguem trocar aquela pergunta por `ZonaMorta` (que responde "nao"
	/// ate o commit), o bombardeio passaria a **acelerar** uma morte que ja esta em curso, e o mundo
	/// cairia antes da hora sem que nada ficasse vermelho.
	/// </summary>
	private ProvaDaMorte AsBordasDoK() => new()
	{
		Nome = "PROVA 9 -- A BORDA QUE FALTAVA: atirar num mundo que JA ESTA AGONIZANDO",
		Frase = "o pavio lento ja esta aceso; o tiro nao pode reiniciar nem acelerar nada",
		Provas = c =>
		{
			ZoneKey alvo = Cobaia;
			(_, double g) = MedidasDoPlaneta(alvo);
			double portao = MortePlanetaria.BpExigido(g);
			if (DiscoDe(alvo) is not { } disco) { c("(montagem) a cobaia esta na carta estelar", false); return; }

			ComecarMorteLenta(alvo, 999, "prova-agonia");
			for (int i = 0; i < 250; i++) TickDaDestruicao(1);

			EstadoDaMorte? e = MorteDaZona(alvo);
			c("(montagem) o mundo esta no PAVIO LENTO, num estagio adiantado",
			  e is { Fase: FaseDaMorte.Morrendo, Estagio: >= 1 }, $"{e?.Fase} estagio {e?.Estagio}");

			int estagioAntes = e!.Estagio;
			double faltamAntes = e.Faltam;

			for (int i = 0; i < 20; i++) AtingirMundoComKi(disco, TiroDeProva(portao * 10_000, 2250), null);

			EstadoDaMorte? depois = MorteDaZona(alvo);
			c("**um mundo AGONIZANDO nao acumula ferida** (a vida deixou de existir na condenacao)",
			  FeridaDoMundo(alvo) == 0, $"{FeridaDoMundo(alvo):0.###}");
			c("**e vinte tiros de um monstro nao ACELERAM o pavio de 20 minutos**",
			  depois != null && depois.Estagio == estagioAntes && Math.Abs(depois.Faltam - faltamAntes) < 1e-9,
			  $"estagio {estagioAntes} -> {depois?.Estagio}, faltam {faltamAntes:0.#} -> {depois?.Faltam:0.#}");
			c("...nem o empurram pra explosao antes da hora",
			  depois is { Fase: FaseDaMorte.Morrendo }, $"{depois?.Fase}");

			// A OUTRA METADE, e ela e o que impede a prova acima de ficar verde num sistema em que o
			// tiro nunca fere nada: com o mundo VIVO, o mesmo tiro acumula.
			_mortos.Limpar();
			_feridasDeMundo.Clear();
			AtingirMundoComKi(disco, TiroDeProva(portao * 10, 1), null);
			c("(a outra metade) com o mesmo mundo VIVO, o mesmo tiro fere",
			  FeridaDoMundo(alvo) > 0, $"{FeridaDoMundo(alvo):0.###}");
		},
		Defeitos =
		[
			// O DEFEITO REAL E ESTE, e ele e mudo: `ZonaMorta` so diz "sim" DEPOIS do commit, entao um
			// mundo no pavio lento voltaria a aceitar dano e o bombardeio adiantaria uma morte que ja
			// estava em curso. Nada no jogo ficaria diferente ate um planeta cair antes da hora.
			("a recusa passou a perguntar `ZonaMorta` (o bombardeio acelera o pavio lento)",
			 s => s.MundoJaCondenado = ZonaMorta),

			("a recusa sumiu de vez (tudo aceita dano, inclusive o que ja explodiu)",
			 s => s.MundoJaCondenado = _ => false),
		],
	};

	// =====================================================================
	// PROVA 10 -- O RESCALDO: O RELOGIO QUE DESENHA OS DESTROCOS
	// =====================================================================
	/// <summary>
	/// ============================ O QUE ESTA FAMILIA COBRA, E POR QUE ELA E DO SERVIDOR ============================
	/// O pedido do dono: *"onde ficava o planeta vao ter uns asteroides/rochas... dps de um tempo eles
	/// despawnam pro servidor n ter q ficar gastando tempo de tick pra ver a posicao de asteroides"*.
	///
	/// **A POSICAO DOS ASTEROIDES NAO EXISTE AQUI**, e por isso nao ha nada sobre asteroide nesta
	/// familia. O campo inteiro e funcao pura de `(semente, indice, tempo)` no cliente (ver
	/// `Core.World.DestrocosDeMundo`), o desenho e medido na bancada `--diagagonia`, e o que o servidor
	/// deve e **um numero so**: ha quantos segundos este mundo morreu.
	///
	/// Esse numero e o `EstadoDaMorte.Faltam`, que ja viajava no `S2C.Mortos` -- o que mudou foi ele
	/// parar de congelar em zero no commit e passar a descer pra negativo pela janela. Entao o que se
	/// prova aqui e o RELOGIO, em quatro pontas:
	///
	///   1. ele **anda** (senao quem chega depois nao sabe ha quanto tempo o mundo caiu, e ve um ceu
	///      diferente do de quem estava online -- o oposto do *"server sync"* que o dono grifou);
	///   2. ele **para** no fim da janela (um `double` que so desce vira, num save de anos, um numero
	///      grande demais pra ser preciso onde importa);
	///   3. o Core **concorda** com o servidor sobre quando a janela fecha -- e uma constante so, lida
	///      pelos dois lados, e nao dois prazos com o mesmo nome;
	///   4. e um mundo que volta **do disco** vem com a janela FECHADA, senao todo boot reacende a
	///      explosao de todos os mundos que ja morreram naquele save.
	/// ==========================================================================================================
	/// </summary>
	private ProvaDaMorte ORescaldoDoMundo() => new()
	{
		Nome = "PROVA 10 -- O RESCALDO (o relogio dos destrocos, e o custo dele no tique)",
		Frase = "dps de um tempo eles despawnam pro servidor n ter q ficar gastando tempo de tick pra "
			  + "ver a posicao de asteroides",
		Provas = c =>
		{
			double janela = DestrocosDeMundo.SegundosDaJanela;

			c("(montagem) a cobaia esta viva", !ZonaCondenada(Cobaia));

			// PELA PORTA DE PRODUCAO: ninguem escreve `Fase = Destruido` na mao aqui. A explosao anda
			// os 310 s dela e o commit acontece por conta propria, como no jogo.
			ComecarDestruicao(Cobaia, 1, "prova-do-rescaldo");
			for (int i = 0; i <= (int)MortePlanetaria.SegundosDeExplosao && !ZonaMorta(Cobaia); i++)
				TickDaDestruicao(1);

			EstadoDaMorte? e = MorteDaZona(Cobaia);
			c("(montagem) o mundo caiu, e o relogio dele zera no instante da morte",
			  e is { Fase: FaseDaMorte.Destruido } && Math.Abs(e.Faltam) < 1e-9,
			  $"{e?.Fase}, faltam {e?.Faltam:0.###}");

			// ---- 1. O RELOGIO ANDA ----
			for (int i = 0; i < 10; i++) TickDaDestruicao(1);
			e = MorteDaZona(Cobaia);

			c("**O RELOGIO DO RESCALDO ANDA**: dez segundos depois da morte o servidor sabe dizer que "
			+ "foram dez -- e o cliente desenha os destrocos a partir disso, sem um byte novo no fio",
			  e != null && Math.Abs(e.Faltam + 10) < 1e-9, $"faltam {e?.Faltam:0.###}, esperado -10");

			c("...e o Core concorda que isso ainda esta DENTRO da janela dos destrocos",
			  e != null && DestrocosDeMundo.DentroDaJanela(-e.Faltam),
			  $"-{e?.Faltam:0.#} contra janela de {janela:0}");

			// ---- 2. E ELE PARA, e para EXATAMENTE no fim da janela ----
			for (int i = 0; i < (int)janela + 30; i++) TickDaDestruicao(1);
			e = MorteDaZona(Cobaia);

			c($"**E ELE PARA NO FIM DA JANELA** ({janela:0} s), em vez de descer pra sempre",
			  e != null && Math.Abs(e.Faltam + janela) < 1e-9,
			  $"faltam {e?.Faltam:0.###}, esperado -{janela:0}");

			c("...e o Core concorda que ai a janela FECHOU (o campo de cacos deixa de ser desenhado)",
			  e != null && !DestrocosDeMundo.DentroDaJanela(-e.Faltam),
			  $"-{e?.Faltam:0.#} contra janela de {janela:0}");

			// ---- 3. O MUNDO QUE VOLTA DO DISCO VEM COM A JANELA FECHADA ----
			// A regra mora no `FecharAJanelaDoRescaldo`, que o carregador chama. Ela e exercitada aqui
			// direto (e nao por arquivo) porque esta bancada roda dentro do `PalcoDeMortes`, onde toda
			// gravacao e barrada -- e uma regra que so pode ser testada lendo disco e uma regra que nao
			// vai ser testada.
			var doDisco = new EstadoDaMorte
			{
				Chave = "prova-do-disco", Nome = "Earth",
				Fase = FaseDaMorte.Destruido, Faltam = 0,
			};
			FecharAJanelaDoRescaldo(doDisco);

			c("**UM MUNDO QUE VOLTA DO DISCO VEM COM A JANELA FECHADA** -- senao todo boot reacenderia "
			+ "a explosao de todo mundo que ja morreu neste save",
			  Math.Abs(doDisco.Faltam + janela) < 1e-9 && !DestrocosDeMundo.DentroDaJanela(-doDisco.Faltam),
			  $"faltam {doDisco.Faltam:0.###}");

			// ...E A OUTRA METADE: quem ainda esta MORRENDO nao e tocado. Sem esta linha, um
			// `FecharAJanelaDoRescaldo` que zerasse tudo passaria -- e apagaria o pavio de 20 minutos.
			var noPavio = new EstadoDaMorte
			{
				Chave = "prova-do-pavio", Nome = "Namek",
				Fase = FaseDaMorte.Morrendo, Estagio = 1, Faltam = 300,
			};
			FecharAJanelaDoRescaldo(noPavio);

			c("...e ele NAO encosta em quem ainda esta morrendo (o pavio de 20 minutos retoma de onde "
			+ "parou, que e o motivo de o `Faltam` ser 'o que resta' e nao um instante absoluto)",
			  Math.Abs(noPavio.Faltam - 300) < 1e-9, $"faltam {noPavio.Faltam:0.###}");

			// ---- 4. O CUSTO NO TIQUE, MEDIDO -- com o campo no ar e sem ----
			MedirOCustoDoRescaldo(c);
		},
		Defeitos =
		[
			// A JANELA ZERADA e o defeito MUDO deste sistema: nada quebra no servidor, nenhum log sai,
			// e o unico sintoma e que quem chega na orbita depois do estouro nao ve destroco nenhum --
			// enquanto quem estava online ve. Um defeito que so existe em duas telas ao mesmo tempo.
			("a janela do rescaldo virou zero (quem chega depois nao ve destroco nenhum)",
			 s => s.SegundosDosDestrocos = 0),

			// E O OPOSTO: o relogio nunca para, e o campo de cacos fica no ceu pra sempre.
			("a janela do rescaldo virou eterna (o relogio desce pra sempre e o ceu vira cemiterio)",
			 s => s.SegundosDosDestrocos = 1_000_000),
		],
	};

	/// <summary>
	/// ============================ O CUSTO DOS DESTROCOS NO TIQUE, MEDIDO -- E OS DOIS NUMEROS IMPRESSOS ============================
	/// O dono deu a RAZAO do despawn com todas as letras: *"dps de um tempo eles despawnam pro servidor
	/// n ter q ficar gastando tempo de tick pra ver a posicao de asteroides"*.
	///
	/// O cabecalho do <see cref="RelogioDoRescaldo"/> ja afirmava o custo em numeros -- e afirmava de
	/// uma medida feita **na mao, uma vez, por quem escreveu**. Isso e a mesma coisa que este projeto
	/// chama de "leitura de codigo": vale ate o dia em que alguem poe um `foreach` ali dentro. Aqui a
	/// medida roda TODA rodada, com o codigo de producao, e imprime os dois numeros que o pedido do
	/// dono compara:
	///
	///   * **COM O CAMPO NO AR**: N mundos mortos dentro da janela -- o caso em que o servidor "esta
	///     pagando" pelos destrocos. Sao duas comparacoes de `double` e uma subtracao por mundo;
	///   * **SEM**: os mesmos N com a janela ja fechada -- a saida barata, uma comparacao;
	///   * e o **ZERO** por baixo, que e o que o laco custa sem mundo morto nenhum. Sem ele os dois
	///     numeros de cima incluiriam o custo do resto do `TickDaDestruicao` e nao diriam nada sobre
	///     os destrocos.
	///
	/// **NAO HA POSICAO DE ASTEROIDE EM NENHUM DOS TRES.** E esse o ponto: o pedido do dono foi
	/// atendido de forma mais forte do que ele pediu -- o servidor nunca soube onde as pedras estao, em
	/// nenhum momento da janela. Ver `Core.World.DestrocosDeMundo`.
	/// ============================================================================================================================
	/// </summary>
	private void MedirOCustoDoRescaldo(Checagem c)
	{
		const int Mundos = 128, Voltas = 200;
		double janela = DestrocosDeMundo.SegundosDaJanela;

		// `dt = 0` DE PROPOSITO: o que se mede e o custo de PERGUNTAR, e nao o de o relogio andar. Com
		// dt positivo os 128 mundos sairiam da janela no meio da medida e as duas metades se
		// misturariam -- a segunda volta ja estaria medindo o caso barato.
		double UmaMedida()
		{
			for (int i = 0; i < 20; i++) TickDaDestruicao(0);   // aquece o JIT e o cache
			ulong t0 = Time.GetTicksUsec();
			for (int i = 0; i < Voltas; i++) TickDaDestruicao(0);
			return (Time.GetTicksUsec() - t0) / (double)Voltas;
		}

		double vazio = UmaMedida();

		var chaves = new List<ChaveDePlaneta>();
		for (int i = 0; i < Mundos; i++)
		{
			var ch = new ChaveDePlaneta(false, "CustoDoRescaldo", (ulong)(900_000 + i));
			chaves.Add(ch);
			_mortos.Por(new EstadoDaMorte
			{
				Chave = ch.Texto, Nome = "CustoDoRescaldo",
				Fase = FaseDaMorte.Destruido, Faltam = -1,   // DENTRO da janela: o campo esta no ar
			});
		}
		double noAr = UmaMedida();

		foreach (ChaveDePlaneta ch in chaves)
			if (_mortos.De(ch) is { } e) e.Faltam = -janela;   // a janela fechou: a saida barata
		double fechada = UmaMedida();

		foreach (ChaveDePlaneta ch in chaves) _mortos.Tirar(ch);

		GD.Print($"    --   O CUSTO DO RESCALDO NO TIQUE, com {Mundos} mundos mortos (media de "
			   + $"{Voltas} voltas do `TickDaDestruicao`, que mora no bloco de 1 Hz):");
		GD.Print($"         sem mundo morto nenhum ........ {vazio,7:0.000} us");
		GD.Print($"         COM O CAMPO NO AR ............. {noAr,7:0.000} us  "
			   + $"(+{noAr - vazio:0.000} us, ou {(noAr - vazio) * 1000 / Mundos:0.00} ns por mundo)");
		GD.Print($"         com a janela ja FECHADA ....... {fechada,7:0.000} us  "
			   + $"(+{fechada - vazio:0.000} us)");
		GD.Print($"         o orcamento de um tique a 30 Hz e 33.333 us -- o campo no ar ocupa "
			   + $"{(noAr - vazio) / 33_333 * 100:0.0000}% dele, uma vez por segundo");

		// ============================ E O QUE A MEDIDA MOSTROU, QUE NENHUM CABECALHO PREVIA ============================
		// Os dois numeros de cima sao **iguais** dentro do ruido (medido: 1,93 contra 1,94 us). Ou seja:
		// a diferenca entre "o servidor esta pagando pelos destrocos" e "nao esta" nao aparece no
		// relogio. O que custa nao e a janela -- e ANDAR na lista de mortos (o `_mortos.Todos.ToList()`
		// aloca uma lista de 128 itens por volta), e isso ja acontecia antes de existir destroco.
		//
		// Isso e mais forte do que o pedido do dono, e vale dizer com todas as letras: **nao ha nada
		// pra despawnar**. Ele pediu que os asteroides sumissem pra o servidor parar de gastar tique
		// com eles; aqui o servidor nunca gastou, porque nunca soube onde eles estavam.
		// ==========================================================================================================
		GD.Print($"         os dois numeros do meio sao IGUAIS dentro do ruido ({noAr - vazio:0.00} contra "
			   + $"{fechada - vazio:0.00} us): o que custa e andar na lista, e nao a janela. Nao ha "
			   + "posicao de asteroide em lugar nenhum do servidor.");

		// O CRIVO E O ORCAMENTO, e nao um numero bonito: 1% de um tique (333 us) com 128 mundos mortos
		// e um teto folgado o bastante pra nao reprovar por causa de uma maquina lenta, e apertado o
		// bastante pra pegar o defeito real -- alguem varrendo asteroide aqui dentro. Um laco de 24
		// pedacos por mundo poria isto na casa dos milissegundos.
		c($"**O RESCALDO NAO CUSTA TIQUE**: com {Mundos} mundos mortos e o campo no ar, o servidor "
		+ $"gasta {noAr - vazio:0.00} us por segundo -- porque ele nao guarda posicao de asteroide "
		+ "nenhuma, so pergunta as horas",
		  noAr - vazio < 333, $"{noAr - vazio:0.000} us acima do vazio");
	}
}
