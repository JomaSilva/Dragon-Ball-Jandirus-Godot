namespace Jandirus.Core.Combat;

/// <summary>Que papel o membro tem na sobrevivencia.</summary>
public enum Vitalidade
{
	/// <summary>Braco, perna, rabo: quebrar ou perder atrapalha, mas nao mata.</summary>
	Membro,
	/// <summary>Cabeca, torso, abdomen: abaixo do limiar NOCAUTEIA; perdido, MATA.</summary>
	Nucleo,
	/// <summary>Cerebro, orgaos: vitais, mas so se ferem por propagacao (nunca sao mirados).</summary>
	Interno,
}

/// <summary>
/// A PECA QUE CAI NO CHAO quando um membro e arrancado -- o `icon_state` do `/obj/bodyparts`
/// do BYOND, virado em simbolo.
///
/// E ENUM, e nao nome de animacao, porque o Core nao conhece arte: quem traduz isto no recorte da
/// folha `Body Parts Bloody` e o cliente (`Client/Decalques.cs`). O que o Core garante e a TABELA
/// -- ver <see cref="Body.PecaDe"/>, que e onde o original erra facil.
///
/// SAO DEZ e nao onze porque o rabo nao tem peca propria: ver a tabela.
/// </summary>
public enum PecaDeCorpo : byte
{
	/// <summary>Nao ha peca (o membro nao caiu). Nunca viaja pela rede.</summary>
	Nenhuma = 0,
	Cabeca = 1,
	Cerebro = 2,
	Torso = 3,
	Abdomen = 4,
	Visceras = 5,
	Reprodutor = 6,
	Braco = 7,
	Mao = 8,
	Perna = 9,
	Pe = 10,
}

/// <summary>Uma parte do corpo.</summary>
public sealed class BodyPart
{
	public string Nome = "";
	public Vitalidade Papel = Vitalidade.Membro;

	public double Vida = 100, VidaMax = 100;
	public bool Decepado;

	/// <summary>Peso no sorteio de qual membro o golpe pega.</summary>
	public double Chance = 100;

	/// <summary>
	/// O `regenerationrate` do `/datum/Body` (`mobparts.dm`): quanto ESTE membro se costura sozinho.
	///
	/// **NAO E COSMETICO, E A ESCALA DA ESPERA.** Torso, braco, perna, mao e pe tem 2; abdomen 1,5;
	/// cabeca e orgaos 1; cerebro e reprodutor 0,5; **rabo 0,125**. Ou seja: um rabo quebrado leva
	/// dezesseis vezes o tempo de um braco quebrado. O port curava os onze com o mesmo numero.
	///
	/// Quem transforma isto em vida por segundo e <see cref="Regeneracao.TaxaDoMembro"/> -- aqui so
	/// mora o dado.
	/// </summary>
	public double TaxaDeRegen = 1;

	/// <summary>
	/// Membro ANINHADO: nunca sai no sorteio de um soco. Cerebro, orgaos, mao e pe so se
	/// ferem por propagacao ou por serem levados junto com o membro que os contem.
	/// </summary>
	public bool Aninhado;

	/// <summary>De quem este membro faz parte. Perder o braco leva a mao junto.</summary>
	public string? Dono;

	/// <summary>A zona que o jogador mira pra acertar este membro de proposito.</summary>
	public string Zona = "torso";

	public double Fracao => VidaMax <= 0 ? 0 : Math.Clamp(Vida / VidaMax, 0, 1);

	/// <summary>Quebrado: abaixo de 20% da propria vida. E o limiar de "Broken" do jogo.</summary>
	public bool Quebrado => !Decepado && Fracao < Regras.LimiarQuebra;

	public BodyPart Copiar() => (BodyPart)MemberwiseClone();
}

/// <summary>Os numeros que decidem quando o corpo cede.</summary>
public static class Regras
{
	/// <summary>Abaixo disto o membro esta QUEBRADO; num nucleo, isso e nocaute.</summary>
	public const double LimiarQuebra = 0.20;

	/// <summary>Quanto de um golpe na cabeca/torso escorre pro cerebro/orgaos.</summary>
	public const double Propagacao = 0.20;

	/// <summary>Decepar custa isto do Ki maximo do dono.</summary>
	public const double CustoDeceparKi = 0.20;

	/// <summary>Um membro devolvido por regeneracao volta com esta fracao de vida.</summary>
	public const double VidaAoRegenerar = 0.70;
}

/// <summary>
/// O CORPO em partes -- o `/datum/Body` do BYOND.
///
/// A vida do personagem NAO e um numero proprio: e a MEDIA das partes. Isso muda como o
/// combate funciona -- ninguem morre por acumular dano espalhado; morre porque um lugar
/// especifico cedeu. Mirar importa.
///
/// TRES REGRAS DE LETALIDADE, e so elas:
///   * qualquer NUCLEO (cabeca, torso, abdomen) abaixo de 20% -> NOCAUTE
///   * qualquer NUCLEO perdido ou zerado -> MORTE
///   * braco, perna, rabo: nunca matam nem nocauteiam, por pior que fiquem
/// </summary>
public sealed class Body
{
	public List<BodyPart> Partes = [];

	/// <summary>
	/// O EIXO DA CURA DESTE CORPO -- ver <see cref="PerfilDeRegen"/>. Nasce no comum (Humano) e e
	/// escrito UMA vez, pelo <see cref="CombatState"/>, a partir do genoma da raca.
	/// </summary>
	public PerfilDeRegen Regen = PerfilDeRegen.Comum;

	/// <summary>
	/// Raca que regenera membro perdido: entra em coma em vez de morrer. E o `canheallopped`, e ele
	/// e DERIVADO do genoma -- ver <see cref="PerfilDeRegen.MembroVolta"/>.
	///
	/// Era um `bool` escrito a mao pelo servidor com uma lista de quatro racas cravada
	/// (`"Namekian" or "Majin" or "BioAndroid" or "Shapeshifter"`), e a lista estava errada nos DOIS
	/// sentidos: o DM da `canheallopped = 0` ao Namekuseijin (o `return` proprio do rework) e ao
	/// Shapeshifter (`Regeneration = 2` -> `DeathRegen = 0`), e da `1` a Kai, Meta, Tsujin e Yardrat
	/// (`Regeneration = 10`), que estavam de fora.
	/// </summary>
	public bool RegeneraDecepado => Regen.MembroVolta;

	public static Body Novo(bool comRabo = false)
	{
		var b = new Body();
		void P(string nome, Vitalidade papel, string zona, double chance, double regen,
			   bool aninhado = false, string? dono = null)
			=> b.Partes.Add(new BodyPart
			{
				Nome = nome, Papel = papel, Zona = zona, Chance = chance, TaxaDeRegen = regen,
				Aninhado = aninhado, Dono = dono,
			});

		// O ULTIMO NUMERO E O `regenerationrate` do `/datum/Body` correspondente (`mobparts.dm`):
		// Head 1 (:124), Brain 0,5 (:151), Torso 2 (:163), Abdomen 1,5 (:191), Organs 1 (:208),
		// Reproductive_Organs 0,5 (:229), Arm 2 (:240), Hand 2 (:252), Leg 2 (:266), Foot 2 (:275),
		// Tail 0,125 (:289). Ver <see cref="BodyPart.TaxaDeRegen"/>.
		P("Cabeca", Vitalidade.Nucleo, "cabeca", 40, 1);
		P("Cerebro", Vitalidade.Interno, "cabeca", 0, 0.5, aninhado: true, dono: "Cabeca");
		P("Torso", Vitalidade.Nucleo, "torso", 100, 2);
		// ABDOMEN e uma zona PROPRIA, nao parte do torso. E assim no original: a "virilha" e
		// um alvo separado no seletor, e e por ela que se acerta o rabo e o reprodutor.
		P("Abdomen", Vitalidade.Nucleo, "abdomen", 70, 1.5);
		P("Orgaos", Vitalidade.Interno, "torso", 0, 1, aninhado: true, dono: "Torso");
		// O reprodutor NAO e aninhado no original (`isnested = 0`, `targetchance = 55`): ele
		// sai no sorteio como qualquer outro membro. Aninhado aqui o tornaria inalcancavel, e
		// a celula de virilha do seletor de zona nao teria pra onde mandar o golpe.
		P("Reprodutor", Vitalidade.Membro, "abdomen", 20, 0.5, dono: "Abdomen");

		P("Braco esquerdo", Vitalidade.Membro, "bracos", 60, 2);
		P("Mao esquerda", Vitalidade.Membro, "bracos", 0, 2, aninhado: true, dono: "Braco esquerdo");
		P("Braco direito", Vitalidade.Membro, "bracos", 60, 2);
		P("Mao direita", Vitalidade.Membro, "bracos", 0, 2, aninhado: true, dono: "Braco direito");

		P("Perna esquerda", Vitalidade.Membro, "pernas", 50, 2);
		P("Pe esquerdo", Vitalidade.Membro, "pernas", 0, 2, aninhado: true, dono: "Perna esquerda");
		P("Perna direita", Vitalidade.Membro, "pernas", 50, 2);
		P("Pe direito", Vitalidade.Membro, "pernas", 0, 2, aninhado: true, dono: "Perna direita");

		if (comRabo) P("Rabo", Vitalidade.Membro, "abdomen", 15, 0.125);

		return b;
	}

	public BodyPart? Achar(string nome) => Partes.FirstOrDefault(p => p.Nome == nome);

	/// <summary>
	/// DE QUAL MEMBRO SAI QUAL PECA -- a tabela do BYOND, colada linha a linha.
	///
	/// No original ela mora em DOIS lugares: o `bodypartType` de cada `/datum/Body` diz que OBJETO
	/// nasce no chao (`mobparts.dm:119-324`), e cada objeto declara o proprio `icon_state`
	/// (`mobparts.dm:401-477`). Aqui os dois passos viram um so, porque o unico consumidor daquilo
	/// e o desenho.
	///
	/// ============================ AS DUAS ARMADILHAS, E AS DUAS SAO DO ORIGINAL ============================
	///   * PERNA DESENHA "Limb". `/datum/Body/Leg` aponta pra `/obj/bodyparts/Limb`
	///     (`mobparts.dm:270`), que tem `icon_state = "Limb"` (`:420`) -- NAO "leg", que nem existe
	///     na folha. Qualquer mapeamento espertinho por nome minusculo cai fora exatamente aqui, e o
	///     sintoma seria uma peca errada num evento raro demais pra alguem conferir depois.
	///   * RABO DESENHA "Guts". `/datum/Body/Tail` aponta pra `/obj/bodyparts/Tail`, e esse objeto
	///     declara `icon_state = "Guts"` (`mobparts.dm:477`): a folha nao tem recorte de rabo e o
	///     BYOND reusa visceras. E o que ele faz -- nao e invencao daqui, e nao e pra "consertar".
	/// =======================================================================================================
	///
	/// O DESCONHECIDO VIRA CABECA, e isso tambem e do original: o `/datum/Body` base nasce com
	/// `bodypartType = /obj/bodyparts/Head` (`mobparts_logic.dm:56`), entao um membro sem linha na
	/// tabela ja caia nesse padrao la. Membro novo que apareca aqui cai numa cabeca no chao -- e
	/// visivel, que e melhor do que nao cair nada e ninguem descobrir.
	/// </summary>
	public static PecaDeCorpo PecaDe(string nome) => nome switch
	{
		"Cabeca" => PecaDeCorpo.Cabeca,          // `/obj/bodyparts/Head`,     "Head"     (:406)
		"Cerebro" => PecaDeCorpo.Cerebro,        // `/obj/bodyparts/Brain`,    "Brain"    (:413)
		"Torso" => PecaDeCorpo.Torso,            // `/obj/bodyparts/Torso`,    "Torso"    (:427)
		"Abdomen" => PecaDeCorpo.Abdomen,        // `/obj/bodyparts/Abdomen`,  "Abdomen"  (:434)
		"Orgaos" => PecaDeCorpo.Visceras,        // `/obj/bodyparts/Guts`,     "Guts"     (:469)
		"Reprodutor" => PecaDeCorpo.Reprodutor,  // `/obj/bodyparts/SOrgans`,  "SOrgans"  (:455)
		"Braco esquerdo" or "Braco direito" => PecaDeCorpo.Braco,   // `/obj/bodyparts/Arm`,   "Arm"   (:448)
		"Mao esquerda" or "Mao direita" => PecaDeCorpo.Mao,         // `/obj/bodyparts/Hands`, "Hands" (:441)
		"Perna esquerda" or "Perna direita" => PecaDeCorpo.Perna,   // `/obj/bodyparts/Limb`,  "Limb"  (:420)
		"Pe esquerdo" or "Pe direito" => PecaDeCorpo.Pe,            // `/obj/bodyparts/Foot`,  "Foot"  (:462)
		"Rabo" => PecaDeCorpo.Visceras,          // `/obj/bodyparts/Tail`, mas `icon_state = "Guts"` (:477)
		_ => PecaDeCorpo.Cabeca,                 // o padrao do `/datum/Body` base (mobparts_logic.dm:56)
	};

	/// <summary>
	/// A vida do personagem: MEDIA SIMPLES das partes que ainda existem.
	///
	/// Decepado sai do DENOMINADOR em vez de contar zero -- senao perder um braco derrubaria
	/// a vida de todo mundo em ~7% de graca, e um manco viveria eternamente com meia vida.
	/// </summary>
	public double Vida()
	{
		int n = 0;
		double soma = 0;
		foreach (BodyPart p in Partes)
		{
			if (p.Decepado) continue;
			soma += p.Fracao * 100;
			n++;
		}
		return n == 0 ? 0 : soma / n;
	}

	// =====================================================================
	// ESTADO
	// =====================================================================
	private IEnumerable<BodyPart> Nucleos => Partes.Where(p => p.Papel == Vitalidade.Nucleo);

	/// <summary>Algum nucleo abaixo do limiar: o corpo desliga.</summary>
	public bool DeveNocautear() => Nucleos.Any(p => !p.Decepado && p.Fracao < Regras.LimiarQuebra);

	/// <summary>Algum nucleo zerado ou perdido: fim. Raca que regenera entra em coma.</summary>
	public bool DeveMorrer() => Nucleos.Any(p => p.Decepado || p.Vida <= 0);

	public IEnumerable<BodyPart> Quebrados() => Partes.Where(p => p.Quebrado);
	public IEnumerable<BodyPart> Perdidos() => Partes.Where(p => p.Decepado);

	/// <summary>
	/// "Extremamente ferido" -- o gatilho do Zenkai maior. E a fracao do corpo que esta
	/// quebrada ou perdida.
	/// </summary>
	public bool MuitoFerido(double fracao = 0.45)
	{
		if (Partes.Count == 0) return false;
		int ruins = Partes.Count(p => p.Decepado || p.Quebrado);
		return (double)ruins / Partes.Count >= fracao;
	}

	// =====================================================================
	// DANO
	// =====================================================================
	/// <summary>
	/// Sorteia qual membro o golpe pega. A zona mirada nao GARANTE o membro -- so pesa a
	/// favor dele. Errar a mira e parte do jogo.
	/// </summary>
	public BodyPart? Sortear(string? zona, Random rng)
	{
		var elegiveis = Partes.Where(p => !p.Aninhado && !p.Decepado).ToList();
		if (elegiveis.Count == 0) return null;

		double total = 0;
		var pesos = new double[elegiveis.Count];
		for (int i = 0; i < elegiveis.Count; i++)
		{
			// fora da zona mirada o membro ainda pode ser pego, com um quarto do peso
			double w = elegiveis[i].Chance;
			if (zona != null && elegiveis[i].Zona != zona) w /= 4;
			pesos[i] = Math.Max(w, 0.01);
			total += pesos[i];
		}

		double sorte = rng.NextDouble() * total;
		for (int i = 0; i < elegiveis.Count; i++)
		{
			sorte -= pesos[i];
			if (sorte <= 0) return elegiveis[i];
		}
		return elegiveis[^1];
	}

	/// <summary>
	/// Machuca UM membro. Cabeca e torso escorrem parte do golpe pros aninhados -- e assim
	/// que o cerebro e os orgaos se ferem, ja que ninguem os acerta diretamente.
	/// </summary>
	public void Ferir(BodyPart alvo, double dano, bool letal)
	{
		if (alvo.Decepado || dano <= 0) return;

		alvo.Vida -= dano;

		// Golpe NAO-LETAL nunca leva o membro abaixo do limiar: e o que permite treinar e
		// brigar sem matar sem querer. O piso e o MESMO limiar do nocaute, entao um soco
		// nao-letal chega a nocautear -- so nao chega a matar.
		double piso = letal ? 0 : alvo.VidaMax * Regras.LimiarQuebra * 0.99;
		if (alvo.Vida < piso) alvo.Vida = piso;

		// a propagacao usa o dano CRU, nao o que sobrou depois do piso: um golpe absurdo na
		// cabeca ainda chacoalha o cerebro mesmo com a cabeca protegida pelo nao-letal
		foreach (BodyPart dentro in Partes)
			if (dentro.Dono == alvo.Nome && !dentro.Decepado)
				Ferir(dentro, dano * Regras.Propagacao, letal);
	}

	/// <summary>
	/// Arranca um membro. Leva junto o que estava DENTRO dele -- perder o braco leva a mao.
	///
	/// No BYOND essa cascata existia no papel mas nunca rodava: `parentlimb` nunca era
	/// atribuido, porque a busca devolvia TYPEPATH em vez de instancia. Aqui roda.
	/// </summary>
	public List<BodyPart> Decepar(BodyPart alvo)
	{
		var caiu = new List<BodyPart>();
		if (alvo.Decepado) return caiu;

		alvo.Decepado = true;
		alvo.Vida = 0;
		caiu.Add(alvo);

		foreach (BodyPart dentro in Partes.Where(p => p.Dono == alvo.Nome).ToList())
			caiu.AddRange(Decepar(dentro));

		return caiu;
	}

	/// <summary>Cura espalhada por todos os membros que ainda existem.</summary>
	public void Curar(double quanto)
	{
		foreach (BodyPart p in Partes)
		{
			if (p.Decepado) continue;
			p.Vida = Math.Min(p.Vida + quanto, p.VidaMax);
		}
	}

	/// <summary>
	/// O `SpreadHeal(quanto, FocusVitals = 1)` do DM (`Injuries.dm:89-100`): cura **so os VITAIS, e
	/// so os que estao abaixo de 70%**.
	///
	/// ============================ O PORQUE DE ELE EXISTIR SEPARADO DO <see cref="Curar"/> ============================
	/// E o que `Un_KO()` chama pra por o corpo de pe (`KO.dm:133`, `SpreadHeal(25,1,1)`), e a
	/// diferenca entre ele e uma cura geral e o pedido do dono inteiro: **um braco quebrado NAO
	/// desquebra por levantar do nocaute**. O braco nao e vital, entao ele nao esta nesta lista; o
	/// corte de 70% ainda por cima recusa quem ja estava razoavelmente inteiro.
	///
	/// O port fazia o oposto -- ver `CombatState.Levantar` --, empurrando TODA parte pra 30% de
	/// graca ao fim do nocaute. Nocaute virava botao de cura.
	/// ============================================================================================================
	/// </summary>
	public void CurarVitais(double quanto, double limiar = 0.70)
	{
		foreach (BodyPart p in Partes)
		{
			if (p.Decepado || p.Papel == Vitalidade.Membro) continue;
			if (p.Vida > p.VidaMax * limiar) continue;
			p.Vida = Math.Min(p.Vida + quanto, p.VidaMax);
		}
	}

	/// <summary>
	/// Devolve um membro perdido, com parte da vida -- e o que a regeneracao faz.
	///
	/// ============================ A CASCATA E O IRMAO DA DO <see cref="Decepar"/> ============================
	/// `RegrowLimb()` (`mobparts_logic.dm:128-136`) faz DUAS coisas que este metodo nao fazia, e as
	/// duas sao o espelho da amputacao:
	///
	///   * **RECUSA o aninhado enquanto a raiz esta caida** (`:130-131`). Devolver a mao com o braco
	///     ainda no chao daria uma mao flutuando -- e, pior, gastaria a vez de quem so tem uma;
	///   * **LEVA JUNTO o que estava dentro** (`:132-133`). Perder o braco leva a mao (o `Decepar` ja
	///     fazia isso); devolver o braco tem que devolver a mao, senao a amputacao e permanente pela
	///     metade e ninguem descobre por que a mao nunca volta.
	///
	/// A bancada `cura` foi quem mostrou: o Bio-Androide levava 138 s pra refazer um braco onde o DM
	/// leva 46 -- as tentativas estavam sendo desperdicadas numa mao que voltava sozinha e continuava
	/// contando como membro incompleto pra sempre.
	///
	/// (No DM as duas linhas dependem do `parentlimb`, que a propria base nunca atribui -- o defeito 2
	/// do <see cref="MeleeResolver"/>. Aqui, como o `Dono` existe de verdade, elas rodam.)
	/// ====================================================================================================
	/// </summary>
	public bool Regenerar(BodyPart alvo)
	{
		if (!alvo.Decepado) return false;
		if (alvo.Dono != null && Achar(alvo.Dono) is { Decepado: true }) return false;

		alvo.Decepado = false;
		alvo.Vida = alvo.VidaMax * Regras.VidaAoRegenerar;

		foreach (BodyPart dentro in Partes.Where(p => p.Dono == alvo.Nome && p.Decepado).ToList())
			Regenerar(dentro);

		return true;
	}

	/// <summary>Morrer devolve o corpo inteiro: ninguem renasce sem braco.</summary>
	public void Restaurar()
	{
		foreach (BodyPart p in Partes)
		{
			p.Decepado = false;
			p.Vida = p.VidaMax;
		}
	}

	public Body Copiar()
	{
		var b = new Body { Regen = Regen };
		foreach (BodyPart p in Partes) b.Partes.Add(p.Copiar());
		return b;
	}
}
