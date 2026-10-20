using Godot;
using Jandirus.Core.Forms;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DO BRILHO DA FORMA (`--diagbrilhoforma`, 2026-10-19) -- os dois pedidos do dono desse dia sobre se transformar, na tela e em
/// foto:
///
///   * *"todas as transformaçoes q o personagem consegue se transformar instantaneamente (...) ao se transformar o personagem da um
///     pequeno brilho branco (parecido com o efeito de hit ao tomar dano de um golpe) mas o personagem da um brilho rapido e quando o
///     brilho sai ele ja ta transformado"*;
///   * *"durante cinematica de transformaçao tanto na primeira vez como quando vc n consegue se transformar instantaneamente (isso pra
///     todas as formas) a aura de carga q vc tem caso esteja com ki acima de 100% é desativada ficando apenas a aura da cinematica, e
///     dps q a cinematica acabar caso o ki ainda esteja acima de 100% a aura do ki acima de 100% volta normalmente"*.
///
/// Com janela e com `--host`, um Saiyajin no campo aberto, com a luz travada no meio-dia (`Iluminacao.MeioDiaDeTeste`: o Saiyajin nasce
/// em Vegeta, onde a hora e a do relogio de parede). Os pacotes de forma e de Ki entram pelos MESMOS metodos que a rede chama
/// (`World.AoMudarForma`, `World.AoCairEfeito`): o degrau da cena e escolha do servidor pela maestria, e aqui cada degrau entra na ordem
/// sem depender de treinar maestria nenhuma.
///   A. A INSTANTANEA (base -> SSJ1): no quadro em que o pacote chega o corpo inteiro lava de BRANCO, com a forma JA vestida por baixo e
///      sem o achatamento do soco, e o branco escoa no prazo do Core -- fotos `brilho-1` (o pico), `brilho-2` (escoando) e `brilho-3`
///      (transformado). E uma forma que CRIA camada (a fagulha colada do Legendary) lava a pilha inteira, a camada nova junto: o brilho
///      vem DEPOIS de vestir -- foto `brilho-3b`;
///   B. o que NAO brilha: o pacote de estado (quem chega numa zona com alguem ja transformado) e a volta ao normal;
///   C. a ENCURTADA do SSJ1, no relogio de verdade, com o Ki acima de 100%: a aura de carga acesa antes (foto `brilho-4`), calada na cena
///      com so a chama da cena acesa (foto `brilho-5`), e de volta sozinha quando a cena acaba (foto `brilho-6`);
///   D. a ESTREIA do SSJ1 tambem cala, e a chama volta NO QUADRO em que a cena acaba (o `QueueFree` do fim e adiado: a pergunta e feita
///      antes dele) -- e o Ki que cai de volta no meio de outra cena NAO volta com a aura no fim;
///   E. TODAS as formas: a cena de cada forma do catalogo (a encurtada, ou a unica que ela tem: o Oozaru e a forma de titulo) cala e
///      devolve; a da FURIA nao cala (dono, 2026-10-11: nela a aura que se ve e a do Ki acima de 100%) -- foto `brilho-7`;
///   F. os dois CONTRA-EXEMPLOS, com a mesma regua: a instantanea seca de antes (`World.InstantaneaSecaDeTeste`, foto `brilho-8`) e as
///      duas chamas empilhadas de antes (`CargaVisual.CargaPorCimaDaCenaDeTeste`, foto `brilho-9`).
///
/// Cada foto sai inteira (`user://brilho-N-*.png`) e num recorte de perto em volta do corpo, ampliado (`...-perto.png`).
///
///     Godot --path . --host --rede 9031 --campoteste 8 --horateste 0.5 --diagbrilhoforma --raca Saiyan --conta bancada_brilhoforma
///           --nome Brilho
/// </summary>
public partial class RoboDoBrilhoDaForma : Node
{
	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];

	private static GameClient? C => GameClient.Instance;

	private void Conferir(bool ok, string oque)
	{
		_linhas.Add((ok ? "  ok    " : "  FALHA ") + oque);
		if (!ok) _falhas.Add(oque);
	}

	private void Nota(string linha) => _linhas.Add("  --     " + linha);

	public override void _Ready() => _ = Rodar();

	private async Tarefa Rodar()
	{
		try { await Percurso(); }
		catch (Exception e) { Conferir(false, $"o roteiro correu ate o fim sem excecao ({e.GetType().Name}: {e.Message})"); GD.Print(e.ToString()); }
		Fechar();
	}

	// =====================================================================
	// AS REGUAS
	// =====================================================================

	/// <summary>A aura de carga deste corpo esta DESENHADA agora: o sprite com forca no material, ou a chama divina (SSG, Blue, Rose).</summary>
	private static bool Acesa(CargaVisual c) =>
		(c.DesenhoDeTeste.Visible && (c.DesenhoDeTeste.ForcaNoMaterialDeTeste ?? 0f) > 0.05f) || c.ChamaDeTeste.AcesaDeTeste;

	/// <summary>A chama da CENA esta acesa (a `AuraGrande` do beat, ou a irma divina dela), com forca no material.</summary>
	private static bool ChamaDaCenaAcesa(Transformacao t) =>
		(t.ChamaDaCenaDeTeste.Visible && (t.ChamaDaCenaDeTeste.ForcaNoMaterialDeTeste ?? 0f) > 0.3f) || t.ChamaDivinaDaCenaDeTeste.AcesaDeTeste;

	/// <summary>Como a carga esta, pro rodape das linhas.</summary>
	private static string ComoACarga(CargaVisual c) =>
		$"desenho visivel={c.DesenhoDeTeste.Visible}, forca {c.DesenhoDeTeste.ForcaNoMaterialDeTeste ?? 0f:0.##}, chama divina={c.ChamaDeTeste.AcesaDeTeste}, "
		+ $"excesso={c.ExcessoDeTeste}, calada pela cena={c.CaladaPelaCenaDeTeste}";

	/// <summary>A lavagem do corpo agora, pro rodape das linhas.</summary>
	private static string ComoALavagem(CharacterVisual v) =>
		v.LavagemDeTeste is { } l ? $"mistura {l.Mistura:0.###}, cor #{l.Cor.ToHtml(false)}, achatar {l.Achatar:0.###}" : "sem shader";

	private static float Mistura(CharacterVisual v) => v.LavagemDeTeste?.Mistura ?? -1f;

	private static bool Branco(Color c) => c.R > 0.99f && c.G > 0.99f && c.B > 0.99f;

	/// <summary>As cinematicas vivas em cima deste corpo, agora.</summary>
	private static List<Transformacao> CenasEm(Node2D corpo)
	{
		var l = new List<Transformacao>();
		foreach (Node n in corpo.GetParent().GetChildren())
			if (n is Transformacao t && IsInstanceValid(t) && !t.IsQueuedForDeletion() && t.AlvoDaCena == corpo) l.Add(t);
		return l;
	}

	/// <summary>O pacote de forma, pelo metodo que a rede chama -- e a cena que ele fez nascer no corpo (nula na instantanea).</summary>
	private static Transformacao? Mudar(World mundo, int eu, Node2D corpo, string de, string para, DegrauDeCena degrau)
	{
		var antes = new HashSet<Transformacao>(CenasEm(corpo));
		mundo.AoMudarForma(eu, Catalogo.Rede(de), Catalogo.Rede(para), degrau);
		return CenasEm(corpo).Find(t => !antes.Contains(t));
	}

	/// <summary>Tira a cena da arvore pelo caminho de limpeza do jogo (`Free` -> `_ExitTree` -> `Soltar`), agora.</summary>
	private static void Descartar(Transformacao? t)
	{
		if (t != null && IsInstanceValid(t)) t.Free();
	}

	// =====================================================================
	// O ROTEIRO
	// =====================================================================
	private async Tarefa Percurso()
	{
		bool pronto = await Ate(() => C is { Connected: true, LocalId: > 0 } && World.Instancia?.CorpoDeTeste(C.LocalId) != null, 60);
		Conferir(pronto, "em 60 s o cliente conectou e o corpo local nasceu no mundo");
		if (!pronto || C is not { } cli || World.Instancia is not { } mundo || mundo.CorpoDeTeste(cli.LocalId) is not { } corpo) return;
		int eu = cli.LocalId;
		Iluminacao.MeioDiaDeTeste = true;
		Jandirus.Server.GameServer.Instance?.EmbateDeFoto_CeuLimpo(eu);
		await Segundos(2.0);   // o corpo assenta no campo

		if (corpo.GetNodeOrNull<CharacterVisual>("Visual") is not { } vis
			|| corpo.GetNodeOrNull<CargaVisual>("Carga") is not { } carga)
		{
			Conferir(false, "(premissa) o corpo local tem Visual e Carga");
			return;
		}
		GD.Print("[dbrilho] ===== O BRILHO DA INSTANTANEA E A AURA DE CARGA NA CENA (dono, 2026-10-19) =====");
		string b = Catalogo.IdBase, s1 = Catalogo.IdSsj1;
		Mudar(mundo, eu, corpo, b, b, DegrauDeCena.Nenhuma);
		await Segundos(0.6);
		Conferir(vis.TemCabeloDeTeste && Mistura(vis) is >= 0f and <= 0.01f && !carga.ExcessoDeTeste,
				 $"(premissa) na base, com cabelo pra trocar ({vis.CabeloDeTeste.GetFile()}), o corpo limpo ({ComoALavagem(vis)}) e o Ki abaixo de 100%");
		Fotografar("brilho-0-base");

		// ---- A. a instantanea brilha ----
		string cabeloBase = vis.CabeloDeTeste;
		Transformacao? semCena = Mudar(mundo, eu, corpo, b, s1, DegrauDeCena.Nenhuma);
		(float Mistura, Color Cor, float Achatar)? pico = vis.LavagemDeTeste;
		string cabeloNoPico = vis.CabeloDeTeste;
		Conferir(semCena == null && pico is { } p && p.Mistura >= BrilhoDaInstantanea.Lavagem - 0.01f && Branco(p.Cor) && p.Achatar <= 0.001f,
				 $"A1. A INSTANTANEA BRILHA: no quadro em que o pacote chega (sem cena nenhuma) o corpo lava de BRANCO, no pico do Core, e sem o "
				 + $"achatamento do soco ({ComoALavagem(vis)})");
		Conferir(cabeloNoPico != cabeloBase,
				 $"A2. ...e a forma JA ESTA VESTIDA por baixo do branco: o cabelo trocou no mesmo quadro ({cabeloBase.GetFile()} -> {cabeloNoPico.GetFile()})");
		await Quadros(1);
		Fotografar("brilho-1-pico");
		await Segundos(BrilhoDaInstantanea.Segundos * 0.4);
		float escoando = Mistura(vis);
		Fotografar("brilho-2-escoando");
		await Segundos(BrilhoDaInstantanea.Segundos + 0.2);
		Conferir(escoando is > 0.05f and < 0.95f && Mistura(vis) is >= 0f and <= 0.01f && vis.CabeloDeTeste == cabeloNoPico,
				 $"A3. QUANDO O BRILHO SAI ELE JA ESTA TRANSFORMADO: o branco escoa ({escoando:0.##} no meio) e some em "
				 + $"{BrilhoDaInstantanea.Segundos:0.##} s ({ComoALavagem(vis)}), com o cabelo da forma ({vis.CabeloDeTeste.GetFile()})");
		Fotografar("brilho-3-transformado");

		// A FORMA QUE CRIA CAMADA: o brilho tem que vir DEPOIS de vestir, senao a camada nova nasce fora dele (o `flash_cor` do padrao do
		// shader e mistura zero) -- e um boneco branco com uma fagulha por cima nao e brilho.
		Mudar(mundo, eu, corpo, s1, b, DegrauDeCena.Nenhuma);
		await Segundos(BrilhoDaInstantanea.Segundos + 0.2);
		int camadasNaBase = vis.LavagemDaPilhaDeTeste.Camadas;
		Mudar(mundo, eu, corpo, b, "legendary", DegrauDeCena.Nenhuma);
		(int Camadas, int CoresDistintas, float MenorMistura) pilha = vis.LavagemDaPilhaDeTeste;
		Conferir(vis.ColadasDeTeste > 0 && pilha.Camadas > camadasNaBase && pilha.CoresDistintas == 1
				 && pilha.MenorMistura >= BrilhoDaInstantanea.Lavagem - 0.01f,
				 $"A4. ...E A PILHA INTEIRA BRILHA, a camada que a forma acabou de criar junto: o Legendary veste {vis.ColadasDeTeste} colada(s), a pilha "
				 + $"passa de {camadasNaBase} pra {pilha.Camadas} camadas, {pilha.CoresDistintas} cor(es) de lavagem, a menor mistura {pilha.MenorMistura:0.###}");
		await Quadros(1);
		Fotografar("brilho-3b-legendary-no-pico");
		await Segundos(BrilhoDaInstantanea.Segundos + 0.2);
		Mudar(mundo, eu, corpo, "legendary", b, DegrauDeCena.Nenhuma);
		await Segundos(BrilhoDaInstantanea.Segundos + 0.2);

		// ---- B. o que nao brilha ----
		Mudar(mundo, eu, corpo, b, s1, DegrauDeCena.Nenhuma);
		await Segundos(BrilhoDaInstantanea.Segundos + 0.2);
		Mudar(mundo, eu, corpo, s1, s1, DegrauDeCena.Nenhuma);
		float noEstado = Mistura(vis);
		Mudar(mundo, eu, corpo, s1, b, DegrauDeCena.Nenhuma);
		float naVolta = Mistura(vis);
		Conferir(noEstado is >= 0f and <= 0.01f,
				 $"B1. o pacote de ESTADO (de SSJ1 pra SSJ1: quem chega numa zona com alguem ja transformado) NAO brilha -- nao e ve-lo virar (mistura {noEstado:0.###})");
		Conferir(naVolta is >= 0f and <= 0.01f,
				 $"B2. VOLTAR AO NORMAL nao brilha -- o pedido e 'ao se transformar' (mistura {naVolta:0.###})");

		// ---- C. a encurtada cala a aura de carga, no relogio de verdade ----
		mundo.AoCairEfeito("aura_ki", 1);   // o Ki acima de 100%, pelo canal do servidor
		await Segundos(0.5);
		Conferir(Acesa(carga) && carga.ExcessoDeTeste && !carga.SegurandoDeTeste,
				 $"C0. (premissa) com o Ki acima de 100% a aura de carga ACENDE sozinha, sem tecla C ({ComoACarga(carga)})");
		Fotografar("brilho-4-ki-acima-antes-da-cena");
		Transformacao? curta = Mudar(mundo, eu, corpo, b, s1, DegrauDeCena.Curta);
		Cinematica? esperada = Def(s1) is { } d1 ? Cinematicas.NoDegrau(d1, DegrauDeCena.Curta) : null;
		Conferir(curta != null && esperada != null && ReferenceEquals(curta.CenaDeTeste, esperada),
				 $"C1. a ENCURTADA do SSJ1 nasce no corpo ({esperada?.Segundos ?? 0:0.#} s)");
		if (curta == null) return;
		Conferir(!Acesa(carga) && carga.CaladaPelaCenaDeTeste && carga.ExcessoDeTeste,
				 $"C2. NA CENA A AURA DE CARGA SE CALA no quadro em que ela nasce -- o excesso continua escrito (e do servidor), so o desenho apaga ({ComoACarga(carga)})");
		bool chamaDaCena = await Ate(() => IsInstanceValid(curta) && ChamaDaCenaAcesa(curta), 8);
		await Segundos(0.6);   // a chama da cena cresce nos primeiros instantes
		bool soAChamaDaCena = IsInstanceValid(curta) && ChamaDaCenaAcesa(curta) && !Acesa(carga);
		Conferir(chamaDaCena && soAChamaDaCena,
				 $"C3. ...e com a chama DA CENA acesa (aos {(IsInstanceValid(curta) ? curta.TempoDeTeste : -1):0.0} s), a de carga continua apagada: fica so a aura da "
				 + $"cinematica ({ComoACarga(carga)})");
		Fotografar("brilho-5-so-a-aura-da-cena");
		bool acabou = await Ate(() => !IsInstanceValid(curta) || curta.IsQueuedForDeletion() || !curta.Rodando, (esperada?.Segundos ?? 12) + 4);
		await Quadros(3);   // o `QueueFree` do fim da cena so tira o node no fim do quadro, e o `Soltar` vem dele
		Conferir(acabou && !carga.CaladaPelaCenaDeTeste && Acesa(carga),
				 $"C4. A CENA ACABOU E A AURA DO KI ACIMA DE 100% VOLTA SOZINHA -- o Ki continua acima ({ComoACarga(carga)})");
		// A FOTO ESPERA A POEIRA DO FIM BAIXAR: a cena acaba levantando a nuvem da cratera (o beat de cauda), e 0,4 s depois dela a nuvem
		// cobria o corpo e a chama inteiros (a primeira rodada). A medida de cima e do quadro do fim; a foto e pro olho.
		await Segundos(3.5);
		Fotografar("brilho-6-a-aura-do-ki-volta-depois-da-cena");

		// ---- D. a estreia tambem, e o Ki que cai no meio nao volta ----
		Mudar(mundo, eu, corpo, s1, b, DegrauDeCena.Nenhuma);
		await Segundos(BrilhoDaInstantanea.Segundos + 0.2);
		Transformacao? estreia = Mudar(mundo, eu, corpo, b, s1, DegrauDeCena.Estreia);
		Conferir(estreia != null && estreia.CenaDeTeste == Cinematicas.Ssj1 && !Acesa(carga) && carga.CaladaPelaCenaDeTeste,
				 $"D1. na ESTREIA (a cena cheia, a da primeira vez) a aura de carga se cala tambem ({ComoACarga(carga)})");
		if (estreia == null) return;
		// O RELOGIO DA CENA NA MAO: a estreia do SSJ1 tem 27 s, e o que se mede aqui nao e o tempo -- e EM QUE QUADRO a chama volta. O fim
		// bom da cena e um `QueueFree`, adiado pro fim do quadro: a pergunta e feita antes dele, no mesmo quadro em que a cena acabou.
		estreia.SetProcess(false);
		for (int i = 0; i < 1000 && IsInstanceValid(estreia) && estreia.Rodando; i++) estreia._Process(0.1);
		bool acabouAEstreia = IsInstanceValid(estreia) && estreia.FimDeTeste == Transformacao.FimDaCena.Sozinha;
		Conferir(acabouAEstreia && !carga.CaladaPelaCenaDeTeste && Acesa(carga),
				 $"D2. ...e a aura do Ki acima de 100% volta NO QUADRO em que a cena acaba, e nao no seguinte ({ComoACarga(carga)})");
		await Quadros(3);

		// O KI QUE CAI NO MEIO DA CENA: outra encurtada, no mesmo relogio na mao, e o Ki volta pra baixo de 100% na metade dela.
		Mudar(mundo, eu, corpo, s1, b, DegrauDeCena.Nenhuma);
		await Segundos(BrilhoDaInstantanea.Segundos + 0.2);
		Transformacao? meio = Mudar(mundo, eu, corpo, b, s1, DegrauDeCena.Curta);
		bool caiuNoMeio = false;
		if (meio != null)
		{
			meio.SetProcess(false);
			for (int i = 0; i < 1000 && IsInstanceValid(meio) && meio.Rodando; i++)
			{
				meio._Process(0.1);
				if (!caiuNoMeio && meio.TempoDeTeste >= meio.CenaDeTeste.Segundos / 2)
				{
					mundo.AoCairEfeito("aura_ki", 0);
					caiuNoMeio = true;
				}
			}
		}
		await Quadros(3);
		Conferir(caiuNoMeio && !carga.CaladaPelaCenaDeTeste && !Acesa(carga) && !carga.ExcessoDeTeste,
				 $"D3. ...e o Ki que caiu de volta no meio da cena NAO volta com a aura no fim -- 'caso o ki ainda esteja acima de 100%' ({ComoACarga(carga)})");
		mundo.AoCairEfeito("aura_ki", 1);
		await Quadros(2);
		Conferir(Acesa(carga),
				 $"D4. (controle) e o Ki passando de novo dos 100% acende a aura na hora: a cena devolveu a vez ({ComoACarga(carga)})");

		// ---- E. todas as formas, e a furia de fora ----
		Mudar(mundo, eu, corpo, s1, b, DegrauDeCena.Nenhuma);
		await Segundos(BrilhoDaInstantanea.Segundos + 0.2);
		// PRA FORA DAS CRATERAS, pela foto da furia la embaixo: as cenas de C e D abriram o chao e levantaram poeira em volta do corpo, e
		// na primeira rodada a foto da aura na furia saiu tomada por elas. Andar (o piloto automatico, como a `--diagraiva`) e o bastante.
		mundo.AndarDeTeste(Vector2.Right);
		await Segundos(2.5);
		mundo.PararDeTeste();
		await Segundos(1.0);
		Node atores = corpo.GetParent();
		var erradas = new List<string>();
		int cenas = 0;
		foreach (FormaDef d in Catalogo.Todas)
		{
			// O DEGRAU QUE O JOGO DARIA a quem vira sem maestria e sem ser a primeira vez: a encurtada -- ou a unica cena que a forma tem
			// (o Oozaru sempre a cheia; a forma de titulo so tem a estreia).
			DegrauDeCena g = Cinematicas.Degrau(d, estreia: false, maestriaDaForma: 0);
			if (g == DegrauDeCena.Nenhuma) g = DegrauDeCena.Estreia;
			if (d.Id == b || Cinematicas.NoDegrau(d, g) is not { } cena) continue;
			cenas++;
			Transformacao t = Transformacao.Rodar(atores, corpo, d, cena, souEu: false);
			bool calou = carga.CaladaPelaCenaDeTeste && !Acesa(carga);
			Descartar(t);
			bool voltou = !carga.CaladaPelaCenaDeTeste && Acesa(carga);
			if (!calou || !voltou) erradas.Add($"{d.Id}({g}: calou={calou}, voltou={voltou})");
		}
		Conferir(cenas >= 40 && erradas.Count == 0,
				 $"E1. TODAS AS FORMAS: as {cenas} cenas de forma do catalogo (o Oozaru e a forma de titulo inclusos) calam a aura de carga e a "
				 + $"devolvem no fim{(erradas.Count > 0 ? " -- erradas: " + string.Join(", ", erradas) : "")}");
		Transformacao furia = Transformacao.Rodar(atores, corpo, forma: null, Cinematicas.Furia, souEu: false);
		await Segundos(1.2);
		bool furiaComAura = IsInstanceValid(furia) && Acesa(carga) && !carga.CaladaPelaCenaDeTeste;
		Conferir(furiaComAura,
				 $"E2. ...e a cena da FURIA NAO cala: nela a aura que se ve E a do Ki acima de 100% (dono, 2026-10-11: 'apenas a aura normal por conta "
				 + $"do ki acima de 100%') ({ComoACarga(carga)})");
		Fotografar("brilho-7-a-furia-continua-com-a-aura-do-ki");
		Descartar(furia);

		// ---- F. os contra-exemplos, com as mesmas reguas ----
		mundo.AoCairEfeito("aura_ki", 0);
		await Segundos(BrilhoDaInstantanea.Segundos + 0.2);
		World.InstantaneaSecaDeTeste = true;
		try
		{
			Mudar(mundo, eu, corpo, b, s1, DegrauDeCena.Nenhuma);
			float seca = Mistura(vis);
			Conferir(seca is >= 0f and <= 0.01f && vis.CabeloDeTeste != cabeloBase,
					 $"CONTRA-EXEMPLO (defeito injetado: a instantanea SECA de antes): a forma entra no corpo sem brilho nenhum (mistura {seca:0.###}) -- "
					 + "a regua A1 reprovaria");
			await Quadros(1);
			Fotografar("brilho-8-defeito-a-instantanea-seca");
		}
		finally { World.InstantaneaSecaDeTeste = false; }
		Mudar(mundo, eu, corpo, s1, b, DegrauDeCena.Nenhuma);
		await Segundos(0.3);

		mundo.AoCairEfeito("aura_ki", 1);
		await Segundos(0.3);
		CargaVisual.CargaPorCimaDaCenaDeTeste = true;
		Transformacao? empilhada = null;
		try
		{
			empilhada = Mudar(mundo, eu, corpo, b, s1, DegrauDeCena.Curta);
			bool acendeu = await Ate(() => empilhada != null && IsInstanceValid(empilhada) && ChamaDaCenaAcesa(empilhada), 8);
			await Segundos(0.6);
			Conferir(acendeu && empilhada != null && IsInstanceValid(empilhada) && ChamaDaCenaAcesa(empilhada) && Acesa(carga),
					 $"CONTRA-EXEMPLO (defeito injetado: a cena nao cala a carga, o jogo de antes): as DUAS chamas acesas no mesmo corpo, a da cena e a "
					 + $"do Ki ({ComoACarga(carga)}) -- a regua C3 reprovaria");
			Fotografar("brilho-9-defeito-as-duas-auras-empilhadas");
		}
		finally
		{
			CargaVisual.CargaPorCimaDaCenaDeTeste = false;
			Descartar(empilhada);
		}

		mundo.AoCairEfeito("aura_ki", 0);
		Mudar(mundo, eu, corpo, s1, b, DegrauDeCena.Nenhuma);
		await Segundos(0.3);
	}

	private static FormaDef? Def(string id) => Catalogo.Def(id);

	// =====================================================================
	// A FOTO, O RELOGIO E O FIM
	// =====================================================================

	/// <summary>
	/// A TELA INTEIRA E UM RECORTE DE PERTO. O recorte e um quadrado em volta do corpo local, ampliado 2x sem filtro: o brilho e a chama
	/// sao do tamanho do boneco, e numa tela inteira de 1280x720 o boneco tem uns 90 px de altura.
	/// </summary>
	private void Fotografar(string nome)
	{
		Viewport? v = GetViewport();
		Image? img = v?.GetTexture()?.GetImage();
		if (v == null || img == null || img.IsEmpty()) { Nota($"sem foto `{nome}` (headless nao renderiza: rode com janela)"); return; }
		string caminho = ProjectSettings.GlobalizePath($"user://{nome}.png");
		img.SavePng(caminho);
		Nota("foto salva em " + caminho);
		if (World.Instancia?.PosicaoLocal is not { } eu) return;
		Vector2 pes = v.GetFinalTransform() * v.CanvasTransform * eu;
		const int Lado = 320;
		var r = new Rect2I((int)pes.X - Lado / 2, (int)pes.Y - Lado * 2 / 3, Lado, Lado).Intersection(new Rect2I(0, 0, img.GetWidth(), img.GetHeight()));
		if (r.Size.X <= 0 || r.Size.Y <= 0) return;
		Image perto = img.GetRegion(r);
		perto.Resize(r.Size.X * 2, r.Size.Y * 2, Image.Interpolation.Nearest);
		perto.SavePng(ProjectSettings.GlobalizePath($"user://{nome}-perto.png"));
	}

	private async Tarefa Quadros(int n)
	{
		for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	private async Tarefa Segundos(double s)
	{
		double fim = Time.GetTicksMsec() / 1000.0 + s;
		while (Time.GetTicksMsec() / 1000.0 < fim) await Quadros(1);
	}

	private async System.Threading.Tasks.Task<bool> Ate(Func<bool> cond, double segundos)
	{
		double fim = Time.GetTicksMsec() / 1000.0 + segundos;
		while (Time.GetTicksMsec() / 1000.0 < fim)
		{
			if (cond()) return true;
			await Quadros(1);
		}
		return cond();
	}

	private void Fechar()
	{
		World.InstantaneaSecaDeTeste = false;
		CargaVisual.CargaPorCimaDaCenaDeTeste = false;
		Iluminacao.MeioDiaDeTeste = false;
		foreach (string l in _linhas) GD.Print("[dbrilho] " + l);
		int ok = _linhas.Count(l => l.StartsWith("  ok", StringComparison.Ordinal));
		GD.Print($"[dbrilho] ===== {ok} OK, {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[dbrilho]   - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
