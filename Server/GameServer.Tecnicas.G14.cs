using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Skills;
using Jandirus.Core.Stats;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// LOTE G14 (2026-10-13) -- OS VERBS QUE A LOJA PROMETIA E NAO ENTREGAVA.
///
/// O dono: *"tem mts verbs mortos q poderiam ser tirados, e tem verb q diz q faz algo mas n faz nada"*. A resposta
/// tem duas metades. Esta e a dos verbs que NAO esperavam sistema nenhum e so nao tinham sido escritos:
///
///     Afterimage_Toggle     `Ki/misc.dm:91-98`              o `haszanzo` alternado: um bit
///     Zanzoken_Afterimage   `Physical Skills.dm:37-54`      cinco segundos de +20 de deflexao e miragens
///                           `Buff Effects.dm:29-45`
///     Acid_Spit             `Race Trees/arlian.dm:42-74`    a bola que envenena
///
/// A outra metade -- os verbs cujo SISTEMA falta (parar o tempo, a arvore de arma, a magia de palavra...) -- nao esta
/// aqui: eles deixaram de virar botao (`Habilidades.DasSkills`), o aviso de compra nao os anuncia (`EfeitoEmTexto`)
/// e as skills que so prometiam um deles fecharam pra compra (`SkillsFechadas`).
///
/// AS REGRAS DA CASA: numeros 1:1 com o DM, com arquivo:linha ao lado; o que diverge esta dito onde diverge.
///
/// OS TIQUES SAO PELO RELOGIO, e nao por soma de passo: os dois efeitos do DM tem `tick_delay = 5` (meio segundo) e
/// o efetor daqui anda de 0,2 em 0,2 s. Somando passos, o ultimo tique (o que cai em cima do fim do prazo) se perdia
/// -- o veneno batia cinco vezes em vez das seis do original. Cada efeito guarda QUANDO e o proximo tique, e o que
/// venceu roda antes de o prazo o tirar.
/// </summary>
public sealed partial class GameServer
{
	private void RegistrarTecnicasG14()
	{
		IniciarLote("G14");
		Vivo("Afterimage_Toggle", AlternarAMiragemG14);
		Vivo("Zanzoken_Afterimage", MiragensG14);
		Vivo("Acid_Spit", CuspeAcidoG14);
	}

	// =====================================================================
	// 0. ESTADO DO LOTE
	// =====================================================================
	private sealed class MiragensDePeG14
	{
		public long Ate, ProximaEm;
	}

	/// <summary>Quem esta sob o `effect/illusion/Afterimage`: ate quando, e quando sai a proxima miragem (relogio do servidor, ms).</summary>
	private readonly Dictionary<int, MiragensDePeG14> _miragensG14 = [];

	private sealed class VenenoG14
	{
		public int Dono;
		public long Ate, ProximoEm;
	}

	/// <summary>Quem esta envenenado pelo Cuspe Acido: por quem, ate quando, e quando e o proximo tique (ms).</summary>
	private readonly Dictionary<int, VenenoG14> _venenoG14 = [];

	/// <summary>O `blasting = 1 ... sleep(10) ... blasting = 0` do Acid_Spit, por corpo (ms).</summary>
	private readonly Dictionary<int, long> _cuspeProntoG14 = [];

	/// <summary>Este corpo saiu: o que era dele neste lote sai junto -- e a deflexao emprestada volta.</summary>
	private void EsquecerG14(int id)
	{
		if (_miragensG14.Remove(id) && _players.TryGetValue(id, out ServerPlayer? pl) && pl.Combate != null)
			pl.Combate.Deflexao -= DeflexaoDasMiragensG14;
		_venenoG14.Remove(id);
		_cuspeProntoG14.Remove(id);
	}

	/// <summary>O relogio do lote: os `Ticked` e o `Removed` dos dois efeitos do DM. Chamado no tique do efetor.</summary>
	private void TickG14()
	{
		if (_miragensG14.Count == 0 && _venenoG14.Count == 0) return;
		long agora = NowMs();

		foreach (int id in _miragensG14.Keys.ToList())
		{
			MiragensDePeG14 m = _miragensG14[id];
			if (!_players.TryGetValue(id, out ServerPlayer? pl)) { _miragensG14.Remove(id); continue; }

			// `Ticked`: a imagem do corpo fica no tile por um segundo (`Buff Effects.dm:41-45`) -- o vulto do Zanzoken,
			// sem deslocamento
			while (agora >= m.ProximaEm && m.ProximaEm <= m.Ate)
			{
				m.ProximaEm += MsEntreMiragensG14;
				AnunciarZanzo(pl, pl.Pos);
			}
			if (agora < m.Ate) continue;

			// `Removed(target)`: `target.deflection -= 20` (`:38-40`)
			_miragensG14.Remove(id);
			if (pl.Combate != null) pl.Combate.Deflexao -= DeflexaoDasMiragensG14;
		}

		foreach (int id in _venenoG14.Keys.ToList())
		{
			VenenoG14 v = _venenoG14[id];
			if (!_players.TryGetValue(id, out ServerPlayer? alvo) || alvo.Ficha.dead) { _venenoG14.Remove(id); continue; }

			// `Ticked`: `target.SpreadDamage(magnitude/duration,,"Poison")` (`Alchemy Effects.dm:66-67`) -- nao letal
			while (agora >= v.ProximoEm && v.ProximoEm <= v.Ate && !alvo.Ficha.dead)
			{
				v.ProximoEm += MsEntreTiquesDoVenenoG14;
				EspalharDanoG3(alvo, _players.GetValueOrDefault(v.Dono) ?? alvo, DanoDoVenenoPorTiqueG14, letal: false);
			}
			if (agora >= v.Ate) _venenoG14.Remove(id);
		}
	}

	// =====================================================================
	// 1. AFTERIMAGE_TOGGLE -- o `haszanzo` alternado
	// =====================================================================
	/// <summary>
	/// DEFEITO INJETADO (bancada `--v021teste`): a Miragem guardada continua valendo -- o botao escreve o bit e
	/// ninguem o le (o corte escrito e nao aplicado). Lido pelo <see cref="UsaAMiragem"/>. Falso em jogo, sempre.
	/// </summary>
	internal static bool MiragemGuardadaAindaValeDeTeste;

	/// <summary>
	/// ESTE CORPO USA A TECNICA DA MIRAGEM AGORA? -- o `haszanzo` do DM: aprendeu a skill (`after_learn()` o liga) E
	/// nao a guardou (`Afterimage_Toggle`). E a pergunta de todo lugar que decide vulto, piscada e embate.
	/// </summary>
	private bool UsaAMiragem(ServerPlayer? pl) =>
		pl?.Livro?.Sabe(PathDoZanzoken) == true && (!pl.Ficha.zanzoDesligado || MiragemGuardadaAindaValeDeTeste);

	/// <summary>
	/// `Afterimage_Toggle` (`Ki/misc.dm:91-98`): *"You will now use your Afterimage technique (click to use)."* /
	/// *"You will no longer use your Afterimage technique."*. No DM e o proprio `haszanzo` que alterna; aqui o bit
	/// mora na ficha (`Fighter.zanzoDesligado`) e vai pro save com ela.
	/// </summary>
	private void AlternarAMiragemG14(ServerPlayer pl)
	{
		pl.Ficha.zanzoDesligado = !pl.Ficha.zanzoDesligado;
		Avisar(pl, pl.Ficha.zanzoDesligado
			? "voce guarda a tecnica da Miragem: nada de piscar, de vulto nem de embate de Zanzoken ate liga-la de novo."
			: "voce volta a usar a tecnica da Miragem (duplo clique no chao pra piscar).");
	}

	// =====================================================================
	// 2. ZANZOKEN_AFTERIMAGE -- cinco segundos rapido demais pra vista
	// =====================================================================
	/// <summary>`target.deflection += 20` (`Buff Effects.dm:36`).</summary>
	private const double DeflexaoDasMiragensG14 = 20;

	/// <summary>`duration = 50` tiques (`:33`).</summary>
	private const long DuracaoDasMiragensMsG14 = 5000;

	/// <summary>`tick_delay = 5` tiques (`:34`).</summary>
	private const long MsEntreMiragensG14 = 500;

	/// <summary>`usr.stamina<15` (`Physical Skills.dm:47`).</summary>
	private const double VigorDasMiragensG14 = 15;

	/// <summary>
	/// ZANZOKEN AFTERIMAGE (`Physical Skills.dm:37-54`): `kireq = Ephysoff*BaseDrain*0.4`, as condicoes de todo
	/// punho (`!med && !train && !KO && Ki >= kireq && !basicCD && canfight`), mais uma MAO LIVRE (`:40-42`) e 15 de
	/// vigor (`:47`). Ai `flick('Zanzoken.dmi')`, `teleport.wav`, `basicCD += 15` e o efeito `illusion/Afterimage`:
	/// +20 de deflexao por 5 s (a pontaria de quem bate cai vinte pontos -- `CombatMath.Pontaria`) e uma miragem do
	/// corpo a cada meio segundo.
	///
	/// O EFEITO NAO EMPILHA: o mesmo efeito de novo RENOVA o prazo (`max_stacks = 1`, `Effects Master.dm:64`).
	///
	/// O `buffCD` (`:43-45`) e LETRA MORTA no original: o verb o LE, e nada no codigo inteiro o escreve (so o
	/// decremento do `movement handler.dm:112-115`). Nao veio.
	/// (`!unarmed && (weaponeq > 1 || twohanding)`: o port tem `weaponeq`; `unarmed`/`twohanding` nao -- a mesma
	/// leitura do `HokutoG10`.)
	/// </summary>
	private void MiragensG14(ServerPlayer pl)
	{
		Fighter f = pl.Ficha;
		if (!AbrirPunhoG7(pl, 0.4, BasicCdG3, out double custo, cobrar: false)) return;
		if (f.weaponeq > 1) { Avisar(pl, "voce precisa de uma mao livre pra isso."); return; }
		if (f.stamina < VigorDasMiragensG14) { Avisar(pl, "voce esta cansado demais pra isso."); return; }
		CobrarPunho(pl, custo, BasicCdG3);

		long agora = NowMs();
		if (!_miragensG14.TryGetValue(pl.Id, out MiragensDePeG14? m))
		{
			_miragensG14[pl.Id] = m = new MiragensDePeG14();
			if (pl.Combate != null) pl.Combate.Deflexao += DeflexaoDasMiragensG14;
		}
		m.Ate = agora + DuracaoDasMiragensMsG14;
		m.ProximaEm = agora + MsEntreMiragensG14;
		AnunciarZanzo(pl, pl.Pos);   // o `flick('Zanzoken.dmi', usr)` da saida
		Avisar(pl, $"voce passa a se mover rapido demais pra vista: miragens por {DuracaoDasMiragensMsG14 / 1000} s (-{custo:0.#} de energia).");
	}

	// =====================================================================
	// 3. ACID_SPIT -- a bola que envenena
	// =====================================================================
	/// <summary>`e.duration = 30` tiques (`arlian.dm:73`).</summary>
	private const long DuracaoDoVenenoMsG14 = 3000;

	/// <summary>`tick_delay = 5` tiques (`Alchemy Effects.dm:65`).</summary>
	private const long MsEntreTiquesDoVenenoG14 = 500;

	/// <summary>`SpreadDamage(magnitude/duration)` com `magnitude = 2` e `duration = 30` (`arlian.dm:73-74`, `Alchemy Effects.dm:67`).</summary>
	private const double DanoDoVenenoPorTiqueG14 = 2.0 / 30;

	/// <summary>
	/// ACID SPIT (`Race Trees/arlian.dm:42-63`): `if(!med && !train && !KO && Ki >= MaxKi*0.1 && HP >= 10 &&
	/// !blasting)`; `Ki -= 12*BaseDrain`; `Blast_Gain()`; uma bola de `basedamage = 40` (a do Kikoho), `mods =
	/// Ekioff*Ekiskill`, que anda reta (`walk(A, usr.dir)`) e vive `Burnout(50)`; `sleep(10)` de `blasting`. Quem ela
	/// FERE fica envenenado (`DomDeProjetil.Veneno`).
	///
	/// O PORTAO E UM DECIMO DO KI CHEIO, e o preco e outro (os `12*BaseDrain`): sao dois numeros no DM, e ficam dois
	/// aqui -- a recusa fala o maior. O verb do DM recusa calado; aqui ele diz por que, como o resto do port.
	///
	/// DIVERGENCIAS DECLARADAS, as duas de tela: a arte e a bola do corpo (o DM usa `Blasts.dmi`, estado "24", que nao
	/// e uma das folhas de `ArteDeKi`) e ela sai com o estalo da bola comum (o verb do DM nao toca som nenhum; aqui
	/// todo tiro sem som proprio soa pelo tipo -- `SonsDeKi.DoDisparo`).
	/// </summary>
	private void CuspeAcidoG14(ServerPlayer pl)
	{
		Fighter f = pl.Ficha;
		if (EmEspera(pl, _cuspeProntoG14, "voce ainda esta recuperando o folego do ultimo cuspe")) return;

		double custo = 12 * f.BaseDrain();
		if (!PodeAtirar(pl, Math.Max(custo, f.MaxKi * 0.1), out string porque)) { Avisar(pl, porque); return; }
		if (f.HP < 10) { Avisar(pl, "voce esta ferido demais pra isso."); return; }

		_cuspeProntoG14[pl.Id] = NowMs() + 1000;
		f.Ki -= custo;
		GanhoDeKi(pl);
		Disparar(pl, new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Blast,
			BaseDano = 40,              // `A.basedamage = 40`
			Velocidade = 1,             // `walk(A, usr.dir)`: sem atraso
			AlcanceTiles = 30,          // `Burnout(50)`, o mesmo da bola comum
			Nome = "Cuspe Acido",
			Dom = DomDeProjetil.Veneno,
		}, verbo: "Acid_Spit");
	}

	/// <summary>
	/// O `OnSucCollWMob(M)` DO CUSPE (`arlian.dm:65-74`): *"You are poisoned!"* e o `Damage_Duration` de 3 s -- um
	/// tique a cada meio segundo, seis ao todo, cada um espalhando `2/30` de dano pelo corpo. (E pouco: no original
	/// tambem. O que o cuspe tem de forte e a bola.) Envenenar de novo recomeca o prazo, nao empilha.
	/// </summary>
	private void EnvenenarG14(ServerPlayer dono, ServerPlayer alvo)
	{
		long agora = NowMs();
		Avisar(alvo, "voce esta envenenado!");
		_venenoG14[alvo.Id] = new VenenoG14 { Dono = dono.Id, Ate = agora + DuracaoDoVenenoMsG14, ProximoEm = agora + MsEntreTiquesDoVenenoG14 };
	}
}
