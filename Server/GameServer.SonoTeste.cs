using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Races;
using Jandirus.Core.Skills;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--sonoteste` -- O SLEEP DO MAJIN (dono, 2026-10-18): *"a skill de regeneraçao mude o nome pra sleep, e o personagem ficaria 5
/// segundos parados, ao qual ao final ele regeneraria grande parte do seu hp dos membros, mas se for interrompido nesses 5s nada acontece e
/// tem um cooldown de 1 minuto mesmo cancelando, regeneração passiva dos majins mesmo em combate esta mt boa n precisa mexer nela"*.
///
/// Um cliente cru entra pela porta (o recado do login e de uma pessoa), e cada familia mede pela porta de PRODUCAO -- a carga do catalogo,
/// o `PrepararSkills` do login, o `UsarHabilidade`, o tique do sono:
///   C. O CATALOGO -- o Sleep existe com os numeros da `Regenerate` (custo, tier, o gene `Regeneration`); a arvore do Majin o pendura NO
///      LUGAR dela, e as outras cinco arvores continuam com a `Regenerate`; o verb e tecnica viva. Contra-exemplo: o catalogo carregado sem o
///      Sleep (`SonoDoMajin.SemOSonoDeTeste`).
///   L. O LOGIN -- o Majin que tinha a `Regenerate` comprada sai com o Sleep comprado, e com o recado; o Namekuseijin, nao.
///   S. O SONO INTEIRO -- deitado (a ocupacao, a pose, o fio); nos 5 s cada parte ferida se refaz em 60%, a decepada nao volta; a recarga.
///   D. DEITADO NA HORIZONTAL -- a cabeca de quem dorme vai pro leste (pro oeste, de quem olhava pro oeste), pelo Estado da ficha e pelo
///      `Facing` e o `Deitado` do snapshot. Contra-exemplo: a cabeca do nocaute de olhar (`SonoNaVerticalDeTeste`), que o deitava na vertical.
///   I. O QUE ACORDA -- andar, sair do lugar, apanhar, o golpe sem dano, apertar outra habilidade, erguer a guarda, cair: nada se refaz, e a
///      recarga de 1 min corre do mesmo jeito. Contra-exemplo: andar deixa de acordar (`SonoSemInterrupcaoDeTeste`).
///
/// O RELOGIO E PULADO, e so ele: a bancada roda dentro de uma chamada (o servidor nao tica no meio), entao "os 5 s passaram" e o comeco do
/// sono empurrado 5 s pra tras. Os 5 s de relogio de verdade, na tela, sao do robo `--diagsono`.
/// </summary>
public sealed partial class GameServer
{
	private const string ContaDaBancadaDoSono = "bancada_sono_ag";

	private bool _sonoRodando, _sonoJaRodou;
	private int _sonoOk, _sonoFalhou;

	private void AfirmarSono(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _sonoOk++; GD.Print($"[sono]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _sonoFalhou++; GD.PrintErr($"[sono]   FALHA {nome}   [{detalhe}]"); }
	}

	public void RodarBancadaDoSono(int porta)
	{
		if (_sonoRodando || _sonoJaRodou) return;
		_sonoRodando = _sonoJaRodou = true;
		_sonoOk = _sonoFalhou = 0;
		GD.Print("[sono] ================ O SLEEP DO MAJIN (dono, 2026-10-18) ================");

		var cli = new ClienteCruDeTeste();
		var mesa = new MesaDeClientesCrus(this, porta, cli);
		try
		{
			ServerPlayer? pl = mesa.EntraNoMundo(cli, ContaDaBancadaDoSono, "Dorminhoco");
			AfirmarSono("(montagem) o cliente entra pela porta e e jogador, e ha catalogo de skills", pl != null && EhJogador(pl) && _skills != null);
			if (pl == null || _skills == null || pl.Combate == null) return;

			OCatalogoDoSono();
			OLoginDoSono(pl);
			OSonoInteiro(pl);
			ODeitarDoSono(pl);
			OQueAcorda(pl);
		}
		catch (Exception e)
		{
			AfirmarSono("a bancada rodou ate o fim", false, e.ToString());
		}
		finally
		{
			SonoDoMajin.SemOSonoDeTeste = false;
			SonoSemInterrupcaoDeTeste = false;
			SonoNaVerticalDeTeste = false;
			LimparTudoDaBancada();
			mesa.Fecha();
			if (_store != null)
				try
				{
					string caminho = System.IO.Path.Combine(_store.Pasta, ContaDaBancadaDoSono + ".json");
					if (System.IO.File.Exists(caminho)) System.IO.File.Delete(caminho);
				}
				catch (Exception e) { GD.PushWarning($"[sono] nao apaguei '{ContaDaBancadaDoSono}': {e.Message}"); }
			_sonoRodando = false;
			GD.Print($"[sono] ================ {_sonoOk} OK, {_sonoFalhou} FALHA(S) ================");
		}
	}

	// =====================================================================
	// C) O CATALOGO
	// =====================================================================
	private void OCatalogoDoSono()
	{
		GD.Print("[sono] -- C) o catalogo: o Sleep no lugar da Regenerate, so na arvore do Majin --");
		Skill? sono = _skills!.Get(SonoDoMajin.Path), regen = _skills.Get(SonoDoMajin.Regenerate), arvore = _skills.Get(SonoDoMajin.ArvoreDoMajin);
		AfirmarSono("o SLEEP existe, com os numeros da Regenerate (custo, tier, o gene `Regeneration` -- a regeneracao passiva nao muda) e o verb `Sleep`",
				  sono != null && regen != null && sono.Nome == "Sleep" && sono.Custo == regen.Custo && sono.Tier == regen.Tier
				  && regen.Genes.ContainsKey("Regeneration") && sono.Genes.GetValueOrDefault("Regeneration") == regen.Genes["Regeneration"]
				  && sono.Verbos.SequenceEqual([SonoDoMajin.Verbo]),
				  $"custo {sono?.Custo}/{regen?.Custo}; tier {sono?.Tier}/{regen?.Tier}; gene {sono?.Genes.GetValueOrDefault("Regeneration")}/{regen?.Genes.GetValueOrDefault("Regeneration")}; verbos {string.Join(",", sono?.Verbos ?? [])}");
		AfirmarSono("a arvore `Majin Racials` pendura o Sleep NO LUGAR da Regenerate",
				  arvore != null && arvore.Galhos.Contains(SonoDoMajin.Path) && !arvore.Galhos.Contains(SonoDoMajin.Regenerate, StringComparer.OrdinalIgnoreCase),
				  string.Join(", ", arvore?.Galhos ?? []));
		string[] outras = [.. _skills.Arvores.Where(a => a.Path != SonoDoMajin.ArvoreDoMajin
													  && a.Galhos.Contains(SonoDoMajin.Regenerate, StringComparer.OrdinalIgnoreCase)).Select(a => a.Nome)];
		AfirmarSono("...e as OUTRAS CINCO arvores com a Regenerate continuam com ela (Alien, Arlian, Bio-Android, meio-Saiyajin, Heran): o pedido e do Majin",
				  outras.Length == 5 && !_skills.Arvores.Any(a => a.Path != SonoDoMajin.ArvoreDoMajin && a.Galhos.Contains(SonoDoMajin.Path)),
				  string.Join(", ", outras));
		AfirmarSono("o verb `Sleep` e tecnica viva, com a ficha portada (o botao sai sozinho do livro)",
				  Tecnicas.Vivas.Contains(SonoDoMajin.Verbo, StringComparer.OrdinalIgnoreCase) && Tecnicas.Get(SonoDoMajin.Verbo) is { Modo: not Modo.NaoPortada });

		SonoDoMajin.SemOSonoDeTeste = true;
		SkillCatalog semOSono;
		try
		{
			semOSono = SkillCatalog.Parse(Godot.FileAccess.GetFileAsString("res://Assets/Data/skills.json"),
										   Godot.FileAccess.GetFileAsString("res://Assets/Data/skilltrees.json"));
		}
		finally { SonoDoMajin.SemOSonoDeTeste = false; }
		AfirmarSono("(defeito injetado: o catalogo carregado sem o Sleep) a arvore do Majin volta a pendurar a Regenerate e o Sleep nao existe -- a regua de cima REPROVA",
				  semOSono.Get(SonoDoMajin.Path) == null && semOSono.Get(SonoDoMajin.ArvoreDoMajin) is { } velha
				  && velha.Galhos.Contains(SonoDoMajin.Regenerate, StringComparer.OrdinalIgnoreCase));
	}

	// =====================================================================
	// L) O LOGIN
	// =====================================================================
	private void OLoginDoSono(ServerPlayer pl)
	{
		GD.Print("[sono] -- L) o login: o Majin com a Regenerate sai com o Sleep --");
		string raca = pl.Race;
		CharacterSave ComARegenerate() => new()
		{
			Skills = [SonoDoMajin.Regenerate], SkillsCompradas = [SonoDoMajin.Regenerate], MarcosTotais = 5, MarcosLivres = 1,
		};
		try
		{
			pl.Race = SonoDoMajin.Raca;
			PrepararSkills(pl, ComARegenerate());
			List<string> falas = Ouvir(() => AvisarDasAposentadas(pl));
			AfirmarSono("o MAJIN que tinha a Regenerate COMPRADA sai do login com o Sleep comprado -- os marcos nao mudam (1 livre) -- e o recado diz o que mudou",
					  pl.Livro.Sabe(SonoDoMajin.Path) && !pl.Livro.Sabe(SonoDoMajin.Regenerate) && pl.Livro.FoiComprada(SonoDoMajin.Path)
					  && pl.Livro.MarcosLivres == 1 && Disse(falas, "agora se chama Sleep"),
					  $"livres {pl.Livro.MarcosLivres}; {Ultimos(falas)}");

			pl.Race = "Namekian";
			PrepararSkills(pl, ComARegenerate());
			List<string> doNamek = Ouvir(() => AvisarDasAposentadas(pl));
			AfirmarSono("...e o NAMEKUSEIJIN com a mesma skill sai com a Regenerate de sempre, sem recado nenhum",
					  pl.Livro.Sabe(SonoDoMajin.Regenerate) && !pl.Livro.Sabe(SonoDoMajin.Path) && !Disse(doNamek, "Sleep"), Ultimos(doNamek));

			pl.Race = SonoDoMajin.Raca;
			SonoDoMajin.SemOSonoDeTeste = true;
			try { PrepararSkills(pl, ComARegenerate()); }
			finally { SonoDoMajin.SemOSonoDeTeste = false; }
			AfirmarSono("(defeito injetado: sem o Sleep) o Majin sai do login com a Regenerate de antes -- a regua de cima REPROVA",
					  pl.Livro.Sabe(SonoDoMajin.Regenerate) && !pl.Livro.Sabe(SonoDoMajin.Path));
		}
		finally
		{
			pl.Race = raca;
			PrepararSkills(pl, null);
			pl.SonoTrocado = false;
		}
	}

	// =====================================================================
	// S) O SONO INTEIRO
	// =====================================================================
	private void OSonoInteiro(ServerPlayer pl)
	{
		GD.Print("[sono] -- S) os 5 s inteiros: cada parte ferida se refaz em 60%, e a recarga de 1 min --");
		CombatState c = pl.Combate!;
		ProntoParaDormir(pl);
		BodyPart mao = c.Corpo.Achar("Mao esquerda")!;
		try
		{
			foreach (BodyPart p in c.Corpo.Partes) p.Vida = p.VidaMax * 0.3;
			mao.Decepado = true;
			mao.Vida = 0;
			c.SincronizarVida();
			double[] antes = [.. c.Corpo.Partes.Select(p => p.Vida)];

			List<string> falas = ApertarEOuvir(pl, SonoDoMajin.Verbo);
			bool deitou = pl.SonoDesde >= 0 && OcupacaoDe(pl) == Ocupacao.Dormindo && pl.Pose(NowMs(), false) == Protocol.Pose.Nocauteado;
			AfirmarSono("O SLEEP, pelo verb: o corpo DEITA -- a ocupacao `Dormindo` (a tela desenha o \"z\") e a pose deitada -- e o servidor diz o que fazer",
					  deitou && Disse(falas, "você dorme"), $"ocupacao {OcupacaoDe(pl)}; pose {pl.Pose(NowMs(), false)}; {Ultimos(falas)}");
			AfirmarSono("...e o estado viaja no fio: o 17 (ocupacao estendida) volta igual do byte, e o ultimo valor do `enum` e ele",
					  CorpoOcupado.DeByte((byte)Ocupacao.Dormindo) == Ocupacao.Dormindo && Enum.GetValues<Ocupacao>().Max() == Ocupacao.Dormindo);

			TickDosSonos();
			AfirmarSono("no meio do sono nada se refaz ainda (o tique so retrata)",
					  pl.SonoDesde >= 0 && c.Corpo.Partes.Select(p => p.Vida).SequenceEqual(antes));

			pl.SonoDesde -= (long)(SonoDoMajin.Segundos * 1000);   // os 5 s passaram (ver o cabecalho)
			List<string> doFim = Ouvir(TickDosSonos);
			bool refeitas = c.Corpo.Partes.Where(p => !p.Decepado).All(p => Math.Abs(p.Vida - p.VidaMax * 0.9) < 1e-6);
			AfirmarSono("**OS 5 S INTEIROS**: cada parte ferida se refaz em 60% da vida dela (de 30% pra 90%), a decepada NAO volta, e o corpo acorda refeito",
					  pl.SonoDesde < 0 && refeitas && mao.Decepado && mao.Vida == 0 && Disse(doFim, "o sono refez o seu corpo"),
					  $"partes {string.Join(", ", c.Corpo.Partes.Where(p => !p.Decepado).Select(p => $"{p.Nome} {p.Fracao:0.##}"))}; mao decepada {mao.Decepado}; {Ultimos(doFim)}");
			long recarga = pl.SonoLivreEm - NowMs();
			AfirmarSono("...e a recarga de 1 min fica armada", recarga is > 59_000 and <= 60_000, $"{recarga} ms");
			List<string> deNovo = ApertarEOuvir(pl, SonoDoMajin.Verbo);
			AfirmarSono("...e o Sleep apertado de novo ESPERA", pl.SonoDesde < 0 && Disse(deNovo, "ainda não quer dormir"), Ultimos(deNovo));
		}
		finally
		{
			mao.Decepado = false;
			mao.Vida = mao.VidaMax;
			c.SincronizarVida();
		}
	}

	// =====================================================================
	// D) DEITADO NA HORIZONTAL
	// =====================================================================
	/// <summary>
	/// (2026-10-18, a foto da `--diagsono`) O CORPO QUE DORME DEITA NA HORIZONTAL. O quadro de nocaute ja e um desenho deitado de cabeca pro
	/// leste, e a tela o gira pra cabeca que o servidor manda; com a cabeca do nocaute de olhar, quem olhava pro sul dormia de cabeca pro norte
	/// -- deitado NA VERTICAL, que na tela parece EM PE. A cabeca do sono vai no fio pelas duas vias: o Estado da ficha (pro dono do corpo) e o
	/// `Facing` + o `Deitado` do snapshot (pra quem olha).
	/// </summary>
	private void ODeitarDoSono(ServerPlayer pl)
	{
		GD.Print("[sono] -- D) deitado na horizontal: a cabeca de quem dorme vai pro leste (ou pro oeste), pra ele e pra quem olha --");
		(Facing Olhar, bool Dormiu, Facing Ficha, Facing Snapshot, bool DeitadoNoFio) Deitar(Facing olhar)
		{
			ProntoParaDormir(pl);
			pl.Facing = olhar;
			ApertarEOuvir(pl, SonoDoMajin.Verbo);
			EntityState e = EstadoDe(pl, NowMs());
			var r = (olhar, pl.SonoDesde >= 0, (Facing)((pl.Sheet().Estado >> 6) & 3), (Facing)e.Facing, e.Deitado);
			ProntoParaDormir(pl);
			return r;
		}
		string Contar((Facing Olhar, bool Dormiu, Facing Ficha, Facing Snapshot, bool DeitadoNoFio) x) =>
			$"olhando {x.Olhar}: {(x.Dormiu ? "dormiu" : "NAO dormiu")}, ficha {x.Ficha}, snapshot {x.Snapshot}{(x.DeitadoNoFio ? " deitado" : " DE PE")}";

		var certos = new[] { Facing.South, Facing.North, Facing.East, Facing.West }.Select(Deitar).ToList();
		AfirmarSono("**DEITADO NA HORIZONTAL**: olhando pro sul, pro norte ou pro leste a cabeca vai pro LESTE; pro oeste, pro OESTE -- e o fio leva a "
					  + "MESMA cabeca pro dono (o Estado da ficha) e pra quem olha (o `Facing` do snapshot, com o bit `Deitado` aceso)",
					  certos.All(x => x.Dormiu && x.DeitadoNoFio && x.Ficha == x.Snapshot && x.Ficha == (x.Olhar == Facing.West ? Facing.West : Facing.East)),
					  string.Join("; ", certos.Select(Contar)));

		SonoNaVerticalDeTeste = true;
		(Facing Olhar, bool Dormiu, Facing Ficha, Facing Snapshot, bool DeitadoNoFio) comODefeito;
		try { comODefeito = Deitar(Facing.South); }
		finally { SonoNaVerticalDeTeste = false; }
		AfirmarSono("(defeito injetado: a cabeca do nocaute de olhar, o jogo de antes da foto) olhando pro sul ele dorme de cabeca pro NORTE -- deitado "
					  + "na vertical, em pe na tela -- e a regua de cima REPROVA",
					  comODefeito.Dormiu && comODefeito.Ficha is Facing.North or Facing.South, Contar(comODefeito));
	}

	// =====================================================================
	// I) O QUE ACORDA
	// =====================================================================
	private void OQueAcorda(ServerPlayer pl)
	{
		GD.Print("[sono] -- I) o que acorda: nada se refaz, e a recarga corre do mesmo jeito --");
		CombatState c = pl.Combate!;
		(string Nome, Action Incomodo, Action Desfazer)[] incomodos =
		[
			("andar (o pedido de passo)", () => pl.Moving = true, () => pl.Moving = false),
			("sair do lugar (um empurrao de 3 px)", () => pl.Pos += new Vec2(3, 0), () => pl.Pos -= new Vec2(3, 0)),
			("apanhar (um dano pelo funil unico, `CombatState.Ferir`)", () => c.Ferir(c.Corpo.Partes.First(p => !p.Decepado), 1, letal: false), () => { }),
			("um golpe que acerta SEM dano (o relogio de luta renovado)", () => c.EntrarEmCombate(), () => { }),
			("apertar outra habilidade (o voo)", () => UsarHabilidade(pl, "voar"), () => { if (pl.Voando) AlternarVoo(pl); }),
			("erguer a guarda (ALT)", () => ErguerAGuarda(pl, true), () => ErguerAGuarda(pl, false)),
			("cair (o nocaute)", () => c.Nocautear(3), () => c.Levantar()),
		];
		foreach ((string nome, Action incomodo, Action desfazer) in incomodos)
		{
			ProntoParaDormir(pl);
			foreach (BodyPart p in c.Corpo.Partes) p.Vida = p.VidaMax * 0.3;
			c.SincronizarVida();
			UsarHabilidade(pl, SonoDoMajin.Verbo);
			bool dormiu = pl.SonoDesde >= 0;
			List<string> falas = Ouvir(() => { incomodo(); TickDosSonos(); });
			bool acordou = pl.SonoDesde < 0;
			// MESMO QUE OS 5 S "PASSEM" AGORA, nada se refaz: o sono ja se perdeu
			if (pl.SonoDesde >= 0) pl.SonoDesde -= (long)(SonoDoMajin.Segundos * 1000);
			TickDosSonos();
			bool semCura = c.Corpo.Partes.All(p => p.Vida <= p.VidaMax * 0.3 + 1e-6);
			bool recarga = pl.SonoLivreEm - NowMs() > 59_000;
			AfirmarSono($"**{nome.ToUpperInvariant()}** ACORDA: o sono se perde (nada se refaz) e a recarga de 1 min corre do mesmo jeito",
					  dormiu && acordou && semCura && recarga && Disse(falas, "o sono se perdeu"),
					  $"dormiu {dormiu}; acordou {acordou}; sem cura {semCura}; recarga {recarga}; {Ultimos(falas)}");
			desfazer();
		}

		// ---- o contra-exemplo ----
		ProntoParaDormir(pl);
		foreach (BodyPart p in c.Corpo.Partes) p.Vida = p.VidaMax * 0.3;
		c.SincronizarVida();
		SonoSemInterrupcaoDeTeste = true;
		bool refezAndando;
		try
		{
			UsarHabilidade(pl, SonoDoMajin.Verbo);
			pl.Moving = true;
			TickDosSonos();
			pl.SonoDesde -= (long)(SonoDoMajin.Segundos * 1000);
			TickDosSonos();
			refezAndando = c.Corpo.Partes.All(p => p.Vida > p.VidaMax * 0.8);
		}
		finally
		{
			SonoSemInterrupcaoDeTeste = false;
			pl.Moving = false;
		}
		AfirmarSono("(defeito injetado: andar nao acorda) quem se mexe dormindo acorda REFEITO do mesmo jeito -- a regua de cima REPROVA", refezAndando);
		ProntoParaDormir(pl);
		foreach (BodyPart p in c.Corpo.Partes) p.Vida = p.VidaMax;
		c.SincronizarVida();
	}

	/// <summary>O corpo no ponto de dormir: de pe, sem guarda, sem golpe, parado, fora de luta, com o Sleep no livro e a recarga livre.</summary>
	private void ProntoParaDormir(ServerPlayer pl)
	{
		CombatState c = pl.Combate!;
		if (pl.Ficha.KO) c.Levantar();
		if (c.Bloqueando) ErguerAGuarda(pl, false);
		pl.SonoDesde = -1;
		pl.SonoLivreEm = 0;
		pl.Moving = false;
		pl.AtaqueAte = 0;
		c.LutandoDeVerdade = 0;
		pl.Livro.Dar(SonoDoMajin.Path);
	}
}
