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

		/// <summary>(v0.3.0) Em que ponto da roda em volta do alvo o corpo vai aparecer na proxima (-1 = a ronda nao comecou).</summary>
		public int Ponto = -1;
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
			// sem deslocamento. (v0.3.0) COM ALVO, A RONDA: um zanzoken pro ponto seguinte da roda em volta dele, e a miragem fica
			// onde o corpo estava -- ver `RondarOAlvoG14`. Sem alvo, o de sempre: a miragem nasce onde o corpo esta.
			while (agora >= m.ProximaEm && m.ProximaEm <= m.Ate)
			{
				m.ProximaEm += MsEntreMiragensG14;
				if (!RondarOAlvoG14(pl, m)) AnunciarZanzo(pl, pl.Pos, longa: !MiragemCurtaDeTeste);
			}
			if (agora < m.Ate) continue;

			// `Removed(target)`: `target.deflection -= 20` (`:38-40`)
			_miragensG14.Remove(id);
			if (pl.Combate != null) pl.Combate.Deflexao -= DeflexaoDasMiragensG14;
		}

		foreach (int id in _venenoG14.Keys.ToList())
		{
			VenenoG14 v = _venenoG14[id];
			if (!_players.TryGetValue(id, out ServerPlayer? alvo) || EhCadaver(alvo)) { _venenoG14.Remove(id); continue; }

			// `Ticked`: `target.SpreadDamage(magnitude/duration,,"Poison")` (`Alchemy Effects.dm:66-67`) -- nao letal
			while (agora >= v.ProximoEm && v.ProximoEm <= v.Ate && !EhCadaver(alvo))
			{
				v.ProximoEm += MsEntreTiquesDoVenenoG14;
				// (2026-10-27) O VENENO DE OUTRO CORPO E GOLPE DELE, ate com ele fora do jogo: a zona segura o barra. Sem esta linha o tique
				// cairia no autor = vitima, que a zona segura nao barra mais (o pedido 113 -- ver `CombatState.Blindado`).
				if (v.Dono != id && NaZonaSegura(alvo)) continue;
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
	// 2b. (v0.3.0) A RONDA EM VOLTA DO ALVO, E A MIRAGEM QUE DURA
	// =====================================================================
	// O dono (2026-10-15): *"as miragens nesse caso deveriam durar mais q as miragens padroes do zanzoken, pq fica sem sentido a
	// miragem durar milisegundos nesse caso (a miragem padrao ao dar double click se mantem igual a atual, sem alteracao
	// alguma). e a skill de miragem agr se tiver um target o personagem vai ficar dando zanzokens em volta do target criando
	// miragens rodeando o alvo, se n tiver target ai fica o normal, criando miragem por onde vc passa"*. DIVERGENCIA DECLARADA:
	// o DM nao rondava ninguem (o `Ticked` so deixava a imagem no tile, `Buff Effects.dm:41-45`). A miragem longa e a do
	// cliente (`Zanzoken.DuracaoDaTecnica`, 2 s): com uma a cada meio segundo, a roda inteira de quatro fica de pe.

	/// <summary>Quantos pontos tem a roda: os quatro lados do alvo.</summary>
	private const int PontosDaRondaG14 = 4;

	/// <summary>A que distancia do alvo a roda passa, em tiles: colado o bastante pra ser "em volta dele", sem pisar nele.</summary>
	private const int TilesDaRondaG14 = 2;

	/// <summary>Ate onde o alvo pode estar pra ronda acontecer, em tiles -- o alcance do zanzoken que vai ate o marcado.</summary>
	private const int TilesAteOAlvoDaRondaG14 = 15;

	/// <summary>
	/// UM PASSO DA RONDA: com um alvo marcado, vivo e perto, o corpo da um zanzoken pro proximo ponto da roda em volta dele
	/// (no sentido do relogio, a partir do lado em que esta), chega olhando pra ele, e a miragem longa fica onde o corpo
	/// estava. Devolve falso quando nao ha ronda (sem alvo, longe demais, o corpo preso, nenhum ponto livre da roda) -- e ai
	/// quem chama deixa a miragem de sempre.
	/// </summary>
	private bool RondarOAlvoG14(ServerPlayer pl, MiragensDePeG14 m)
	{
		if (MiragemSemRondaDeTeste) return false;
		if (Marcado(pl) is not { } alvo || alvo == pl || EhCadaver(alvo)) return false;
		const float T = ZoneCollision.TileSize;
		if (Vec2.Distance(alvo.Pos, pl.Pos) > TilesAteOAlvoDaRondaG14 * T) return false;
		if (ParalisiaAtiva(pl.Id) || !PodeMexerOCorpo(pl)) return false;

		// O PRIMEIRO PASSO PARTE DO LADO EM QUE O CORPO JA ESTA: o ponto da roda mais perto dele, e a ronda segue dali.
		if (m.Ponto < 0)
		{
			Vec2 lado = pl.Pos - alvo.Pos;
			double angulo = Math.Atan2(lado.Y, lado.X);
			m.Ponto = ((int)Math.Round(angulo / (Math.PI * 2 / PontosDaRondaG14)) % PontosDaRondaG14 + PontosDaRondaG14) % PontosDaRondaG14;
		}

		ZoneCollision? mapa = MapaDoSalto(pl.Zone);
		for (int tentativa = 1; tentativa <= PontosDaRondaG14; tentativa++)
		{
			int ponto = (m.Ponto + tentativa) % PontosDaRondaG14;
			double ang = ponto * (Math.PI * 2 / PontosDaRondaG14);
			Vec2 cru = alvo.Pos + new Vec2((float)Math.Cos(ang), (float)Math.Sin(ang)) * (TilesDaRondaG14 * T);
			int cx = (int)MathF.Floor(cru.X / T), cy = (int)MathF.Floor(cru.Y / T);
			// o CENTRO da celula, e so se ela serve de chao: parede, beirada e agua ficam fora da roda (a mesma pergunta do embaralho)
			if (mapa != null && !mapa.ServeDeChao(cx, cy)) continue;
			Vec2 destino = mapa?.CentroDaCelula(cx, cy) ?? cru;

			Vec2 de = pl.Pos;
			m.Ponto = ponto;
			CravarPosicao(pl, destino);
			Vec2 olhar = alvo.Pos - destino;
			if (olhar.LengthSquared > 1e-4f) pl.Facing = MoveRules.FacingFrom(olhar, pl.Facing);
			AnunciarZanzo(pl, de, longa: !MiragemCurtaDeTeste);
			return true;
		}
		return false;
	}

	/// <summary>DEFEITO INJETADO (bancada `--v030teste`): a miragem da tecnica volta a ser a curta do zanzoken comum. Falso em jogo, sempre.</summary>
	internal static bool MiragemCurtaDeTeste;

	/// <summary>DEFEITO INJETADO (bancada `--v030teste`): com alvo, nada de ronda -- a miragem nasce onde o corpo esta, o jogo de antes. Falso em jogo, sempre.</summary>
	internal static bool MiragemSemRondaDeTeste;

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
	/// ============================ O NERF DO DONO (2026-10-18) -- DIVERGENCIA DECLARADA ============================
	/// *"acido do saibaman ta meio op, da uma nerfada"*. No DM a bola do cuspe e a do Kikoho -- `basedamage = 40`, o maior do jogo
	/// (`arlian.dm:52`) -- com a recarga de uma bola comum (`sleep(10)`, 1 s) e o preco de uma (`12*BaseDrain`). A bola basica, nessa fase do
	/// jogo, tem dano-base de 1 a 4 (`Ekioff * log10(blastskill)`): o cuspe valia de 10 a 40 bolas por tiro, um tiro por segundo. Agora a bola
	/// e a METADE e a recarga o DOBRO -- um quarto do dano por segundo --, e o veneno e o preco continuam os mesmos. Vale pro Arlian tambem: o
	/// verb e um so (`arlian.dm:42`, que a arvore do Saibaman empresta).
	///
	/// ============================ O SEGUNDO NERF (dono, 2026-10-22) ============================
	/// *"ia pedir tb pra ver o acid spit pq tao falando q ele ta MUITO op dando one shot em npcs em torneios"*. Com 20 o cuspe ainda valia de 5 a
	/// 20 bolas basicas por tiro. Perguntado, o dono escolheu o dano-base 5 com a mesma recarga de 2 s: um QUARTO do nerf de 18/10 (um
	/// dezesseis avos do dano por segundo do DM) -- de 1,25 a 5 bolas por tiro, e ele continua a arma de longe mais forte do Saibaman. O veneno
	/// e o preco nao mudam.
	/// ===========================================================================================================
	/// </summary>
	internal const double DanoDoCuspeG14 = 5;

	/// <summary>A recarga do cuspe, em ms -- ver <see cref="DanoDoCuspeG14"/> (no DM, `sleep(10)`: 1 s; o segundo nerf a manteve).</summary>
	internal const long MsDaRecargaDoCuspeG14 = 2000;

	/// <summary>DEFEITO INJETADO (`--chefesteste`): o cuspe volta a ser o do DM -- 40 de dano-base e 1 s de recarga. Falso em jogo, sempre.</summary>
	internal static bool CuspeComoNoDmDeTeste;

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

		_cuspeProntoG14[pl.Id] = NowMs() + (CuspeComoNoDmDeTeste ? 1000 : MsDaRecargaDoCuspeG14);
		f.Ki -= custo;
		GanhoDeKi(pl);
		Disparar(pl, new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Blast,
			BaseDano = CuspeComoNoDmDeTeste ? 40 : DanoDoCuspeG14,   // `A.basedamage = 40` no DM -- ver `DanoDoCuspeG14`
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
