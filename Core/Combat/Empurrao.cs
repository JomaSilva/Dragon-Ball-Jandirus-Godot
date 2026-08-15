using Jandirus.Core.Stats;

namespace Jandirus.Core.Combat;

/// <summary>O que um golpe faz ao corpo de quem apanha, alem do dano.</summary>
public enum EfeitoDeImpacto
{
	/// <summary>Nada: o golpe foi fraco demais pra mexer com o corpo.</summary>
	Nada,

	/// <summary>`/effect/slow`: um passo pra tras e o pe pesado por 1 s.</summary>
	Lento,

	/// <summary>`/effect/stagger`: dois passos cambaleando e 1 s desequilibrado.</summary>
	Cambaleia,

	/// <summary>`/effect/knockback`: o corpo VOA -- e o KB do original.</summary>
	Arremesso,
}

/// <summary>
/// KNOCKBACK -- o corpo arremessado pelo golpe.
///
/// ============================ DE ONDE VEM, LINHA POR LINHA ============================
/// `Modules/Movement Improvement/Impact.dm` (quem decide) e `Modules/Stats/Effects/Movement
/// Effects.dm` (o efeito `/effect/knockback`, quem executa).
///
///   dmg = min(dmg, 30)                                          // teto de 30 "tiles" de forca
///   threshold = 4 * M.Ewillpower * M.hpratio * BPModulus(M.expressedBP, expressedBP)
///
///   dmg > threshold || unavoidable  -> ARREMESSO
///        dur = round(min(max(dmg,5)/1.5, 9), 1)
///   dmg > 0.5*threshold             -> CAMBALEIA (2 passos, efeito de 10 tiques)
///   dmg > 1                         -> LENTO     (1 passo, efeito de 10 tiques)
///
/// E o efeito em si:
///
///   duration  = min(kbdur, 10)      // tiques; `tick_delay = 1` = 1 decimo de segundo cada
///   Ticked:   step(target, dir, 16); step(target, dir, 16)   // DOIS passos por tique
///   Added:    KB += 1; canfight -= 1; icon_state = "KB"; toca 'throw.ogg'
///   Removed:  KB -= 1; canfight += 1; cratera no chao; toca 'landharder.ogg'
///
/// O comentario do proprio jogo explica o teto de 10: "anda ~2 tiles/tick, entao 10 ticks ~= 20
/// tiles. Mantem o lado forte sendo arremessado, mas SEM voar o mapa inteiro".
/// =======================================================================================
///
/// ============================ E O QUE O CORPO ENCONTRA NO CAMINHO ============================
/// O `Ticked` nao so move: ele testa a celula de destino a cada tique.
///
///   parede densa  -> se `pow >= T.Resistance && T.destroyable`, a parede e DESTRUIDA e o voo
///                    continua; senao o voo PARA ali e o corpo leva `SpreadDamage(duration)`
///   obj fragil    -> leva `takeDamage(pow)`
///   outro mob     -> OS DOIS levam `SpreadDamage(duration)` e o voo para
///
/// A forca do voo (`pow`) e o `expressedBP` de QUEM BATEU, e nao o dano: e por isso que um golpe
/// fraco de alguem muito forte ainda derruba a parede.
/// =============================================================================================
///
/// Mora no Core, e nao no servidor, pelo mesmo motivo de sempre: e formula do jogo. O servidor
/// executa, o banco de prova mede, e a regra tem UMA casa.
/// </summary>
public static class Empurrao
{
	/// <summary>`dmg = min(dmg,30)` -- o teto de forca antes de virar duracao.</summary>
	public const double TetoDeForca = 30;

	/// <summary>`duration = min(kbdur,10)` -- o teto de tiques de voo.</summary>
	public const int TiquesMax = 10;

	/// <summary>`tick_delay = 1` -- um decimo de segundo por tique.</summary>
	public const double SegundosPorTique = 0.1;

	/// <summary>Dois `step(...,16)` por tique. Em pixels: dois tiles.</summary>
	public const double TilesPorTique = 2;

	/// <summary>Quantos tiques dura o cambaleio e a lentidao (`duration = 10`).</summary>
	public const int TiquesDeTropeco = 10;

	/// <summary>
	/// O LIMIAR: `4 * Ewillpower * hpratio * BPModulus(alvoBP, atacanteBP)`.
	///
	/// Repare no sentido do BPModulus -- e o do ALVO sobre o do ATACANTE. Quem apanha de alguem
	/// muito mais forte tem o limiar despencando, e por isso voa com qualquer golpe; quem apanha
	/// de um mais fraco quase nao se mexe. E tambem por isso que o limiar cai junto com a vida
	/// (`hpratio`): corpo machucado voa mais longe.
	/// </summary>
	public static double Limiar(Fighter alvo, double bpDoAtacante) =>
		4 * alvo.Ewillpower * alvo.hpratio * CombatMath.BpModulus(alvo.expressedBP, bpDoAtacante);

	/// <summary>
	/// O QUE ESTE GOLPE FAZ COM O CORPO. Devolve o efeito e, no arremesso, quantos TIQUES ele voa.
	///
	/// <paramref name="inevitavel"/> e o `unavoidable` do original: alguns golpes arremessam sem
	/// consultar o limiar (o segundo ramo do soco leve, ver <see cref="DoSoco"/>).
	/// </summary>
	public static (EfeitoDeImpacto Efeito, int Tiques) Avaliar(double dmg, double limiar, bool inevitavel)
	{
		dmg = Math.Round(Math.Min(dmg, TetoDeForca));

		if (dmg > limiar || inevitavel)
		{
			// `round(min(max(dmg,5)/1.5, 9), 1)` e depois `min(..., 10)` no efeito. Os dois tetos
			// existem no original e sao diferentes -- o 9 aqui e o 10 la --, entao o de fora nunca
			// morde. Fica copiado como esta pra a conta bater numero a numero.
			double dur = Math.Round(Math.Min(Math.Max(dmg, 5) / 1.5, 9));
			return (EfeitoDeImpacto.Arremesso, (int)Math.Min(dur, TiquesMax));
		}
		if (dmg > 0.5 * limiar) return (EfeitoDeImpacto.Cambaleia, TiquesDeTropeco);
		if (dmg > 1) return (EfeitoDeImpacto.Lento, TiquesDeTropeco);
		return (EfeitoDeImpacto.Nada, 0);
	}

	/// <summary>
	/// O GATILHO, do lado de quem bate (`attack cmn.dm:110-116`):
	///
	///   check = (Ephysoff + Etechnique) / (M.Ephysdef + M.Etechnique) * BPModulus(meu, dele)
	///
	///   golpe LEVE (Type == 1):
	///       check > 3 && prob(check*10)  -> Impact(dmg)          // arremesso normal
	///       senao      prob(check*20)    -> Impact(1, unavoidable) // empurraozinho garantido
	///   golpe PESADO:
	///       sempre                       -> Impact((dmg + Ephysoff*2 + 1) * check)
	///
	/// O segundo ramo do leve e o que da PESO a troca de socos: mesmo sem forca pra arremessar,
	/// um em cada tantos golpes tira o outro do lugar.
	///
	/// **O RAMO DO PESADO ACIMA E O DO DM, E NAO O DESTE PORT**: aqui ele tambem sorteia, a pedido do
	/// dono. A divergencia esta escrita por extenso no <see cref="DoSoco"/>, que e quem decide.
	/// </summary>
	public static double Check(Fighter a, Fighter d) =>
		(a.Ephysoff + a.Etechnique) / Math.Max(d.Ephysdef + d.Etechnique, 0.0001)
		* CombatMath.BpModulus(a.expressedBP, d.expressedBP);

	/// <summary>A forca do golpe pesado: `(dmg + Ephysoff*2 + 1) * check`.</summary>
	public static double ForcaDoPesado(double dmg, Fighter a, double check) =>
		(dmg + a.Ephysoff * 2 + 1) * check;

	/// <summary>
	/// A CHANCE DE ARREMESSAR, POR UNIDADE DE PESO DO GOLPE -- o `10` do `prob(check * 10)` do DM
	/// (`attack cmn.dm:113`). O leve pesa 1 e o pesado pesa 3 (`Protocol.PesoDoGolpe`), entao a
	/// mesma formula da `check*10` pro leve e `check*30` pro pesado.
	/// </summary>
	public const double ChancePorPesoDoGolpe = 10;

	/// <summary>
	/// `prob(check * 10 * peso)` -- a chance percentual de o golpe arremessar. Passa de 100 quando o
	/// atacante e muito mais forte, e e isso mesmo: o `prob()` do DM tambem satura.
	/// </summary>
	public static double ChanceDeArremesso(double check, double peso) =>
		check * ChancePorPesoDoGolpe * peso;

	/// <summary>
	/// O SOCO QUE ENCOSTOU MEXEU COM O CORPO? -- o UNICO lugar que decide isso, pros dois golpes.
	///
	/// ============================ A UNICA DIVERGENCIA DELIBERADA DO DM ============================
	/// **O DM NAO SORTEIA O PESADO.** O `else` de `attack cmn.dm:115-116` cai direto no
	/// `Impact((dmg + Ephysoff*2 + 1) * check)`, e o port copiava isso ao pe da letra. Medido: com
	/// dois Saiyajins de BP 5.551, **100% dos pesados que encostam arremessam** em qualquer razao de
	/// BP >= 1,0x, jogando o corpo 517 a 576 px em menos de um segundo. Contra a IA (que pede pesado
	/// em 35% dos golpes, e ate 85% com raiva cheia) isso vira um corpo no ar a cada 1,6 s -- e mais
	/// da metade da briga e o outro correndo atras. O dono viu e chamou pelo nome: *"era UM JOGANDO O
	/// OUTRO PRA LONGE e tava estranha a luta"*.
	///
	/// **O PEDIDO DELE E DE FREQUENCIA, e o numero e a propria formula do DM**, so que aplicada ao
	/// pesado: `prob(check * 10 * peso)`. Com `PesoDoGolpe(Pesado) = 3` isso da `prob(check*30)` --
	/// 33% em BP parelho, contra os 21,7% do leve. E "uma chance UM POUCO MAIOR" que a do leve, que
	/// foi o que ele pediu, sem constante inventada: o `10` ja existe em `attack cmn.dm:113` e o `3`
	/// ja existe em `Protocol.PesoDoGolpe`.
	///
	/// **O SORTEIO VETA SO O ARREMESSO.** O `Cambaleia` e o `Lento` continuam saindo do `Avaliar`
	/// exatamente como antes -- e de proposito: a faixa do meio e o unico efeito que o pesado tem
	/// contra quem e mais forte que ele (0,5x -> 92,8% de cambaleio, medido), e derrubar isso junto
	/// deixaria o pesado fraco tambem MUDO. Pelo mesmo motivo o ramo falho NAO cai no `Cambaleia`:
	/// isso engordaria em 67% um caminho que hoje nao manda aviso nenhum pro cliente.
	/// =============================================================================================
	/// </summary>
	/// <param name="peso">`Protocol.PesoDoGolpe`: 1 no leve, 3 no pesado. O Core nao conhece o `Net`.</param>
	/// <param name="garantido">
	/// PULA O SORTEIO -- o golpe arremessa se encostou. Nao e do DM: e pros golpes que ja nasceram
	/// como "isto TEM que jogar o outro longe", hoje o golpe de saida do Zanzo Clash
	/// (`GameServer.ZanzoClash.GolpeDeSaida`), que o dono pediu com arremesso certo pra fechar a cena.
	/// </param>
	public static (EfeitoDeImpacto Efeito, int Tiques) DoSoco(
		double dmg, double peso, Fighter a, Fighter d, Func<double, bool> prob, bool garantido = false)
	{
		double check = Check(a, d);
		double limiar = Limiar(d, a.expressedBP);
		double chance = ChanceDeArremesso(check, peso);
		bool pesado = peso > 1;

		if (garantido)
			return Avaliar(pesado ? ForcaDoPesado(dmg, a, check) : dmg, limiar, inevitavel: true);

		// ---- LEVE: os DOIS sorteios do DM, sem uma virgula de diferenca ----
		if (!pesado)
		{
			if (check > 3 && prob(chance)) return Avaliar(dmg, limiar, false);
			if (prob(chance * 2)) return Avaliar(1, limiar, true);   // o `prob(check*20)`
			return (EfeitoDeImpacto.Nada, 0);
		}

		// ---- PESADO: a forca e a mesma do DM; so o ARREMESSO passou a ser sorteado ----
		(EfeitoDeImpacto efeito, int tiques) = Avaliar(ForcaDoPesado(dmg, a, check), limiar, false);
		if (efeito != EfeitoDeImpacto.Arremesso) return (efeito, tiques);
		return prob(chance) ? (efeito, tiques) : (EfeitoDeImpacto.Nada, 0);
	}

	/// <summary>
	/// A RESISTENCIA PADRAO de todo turf e todo obj: `turf/var/Resistance = 20` e
	/// `obj/var/Resistance = 20` (`Modules/Turfs/Building/buildable.dm:345-360`).
	///
	/// VINTE E BAIXO DE PROPOSITO no original: qualquer personagem ja nasce com BP muito acima
	/// disso, entao o cenario e destrutivel desde o primeiro soco. O que protege um pedaco do mapa
	/// nao e a resistencia, e o `destroyable = 0` -- a borda do mundo, as paredes da arena, o
	/// espaco. Construcao de jogador sobe pra `intBPcap` (o teto de BP de quem construiu), e ai
	/// so alguem daquele nivel derruba.
	/// </summary>
	public const double ResistenciaPadrao = 20;
}
