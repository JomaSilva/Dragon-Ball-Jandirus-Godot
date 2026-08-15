namespace Jandirus.Core.Combat;

/// <summary>
/// AS REGIOES DO CORPO QUE O SPRITE SABE DESENHAR.
///
/// Sao as MESMAS cinco zonas de mira que o jogo ja tem (`Protocol.Zonas`, e o campo
/// <see cref="BodyPart.Zona"/>) -- de proposito. Mirar na cabeca, quebrar a cabeca e ver a
/// cabeca ferida tem que ser a mesma divisao do corpo, senao o jogador acerta um lugar e a
/// ferida aparece noutro.
/// </summary>
public enum ZonaDeFerida : byte
{
	Cabeca = 0,
	Torso = 1,
	Abdomen = 2,
	Bracos = 3,
	Pernas = 4,
}

/// <summary>
/// QUANTO CADA REGIAO DO CORPO ESTA MARCADA -- o que o shader desenha.
///
/// ============================ POR QUE ISTO NAO E "A VIDA" ============================
/// A vida do personagem e uma media das partes (`Body.Vida`), e uma media nao diz ONDE doeu.
/// Um lutador com 70% de vida porque levou tudo na perna e um lutador com 70% espalhado sao a
/// mesma barra e dois desenhos completamente diferentes. Esta mascara guarda a diferenca.
/// =====================================================================================
///
/// CABE EM CINCO BYTES: um por regiao, meio byte pra cada camada. Isso importa porque ela
/// viaja por jogador -- ver o pacote de feridas em `Protocol`.
/// </summary>
public readonly struct MascaraDeFeridas : IEquatable<MascaraDeFeridas>
{
	public const int Zonas = 5;

	/// <summary>Quantos degraus cada camada tem. 4 bits: 0 a 15.</summary>
	public const int Degraus = 15;

	/// <summary>Um byte por zona: nibble alto = hematoma, nibble baixo = sangue.</summary>
	private readonly byte _z0, _z1, _z2, _z3, _z4;

	/// <summary>
	/// OS MEMBROS QUE NAO ESTAO MAIS LA, em quatro bits.
	///
	/// ============================ POR QUE ISTO NAO CABE NAS ZONAS ============================
	/// A zona "bracos" e UMA regiao com DOIS bracos dentro. Pra pintar sangue isso basta -- um
	/// braco machucado suja a mesma area. Pra ARRANCAR nao basta: apagar a regiao inteira levaria
	/// os dois bracos junto, e quem perdeu um so ficaria sem nenhum.
	///
	/// Por isso os quatro membros que somem tem bit proprio. Os outros nao precisam: arrancar
	/// cabeca, torso ou abdomen MATA na hora, entao nao existe um corpo andando sem eles pra
	/// desenhar -- eles ficam so com o sangue no maximo, que a curva ja da pro decepado.
	/// ========================================================================================
	/// </summary>
	[Flags]
	public enum Membro : byte
	{
		Nenhum = 0,
		BracoEsq = 1, BracoDir = 2,
		PernaEsq = 4, PernaDir = 8,
	}

	private readonly byte _amp;

	public MascaraDeFeridas(byte z0, byte z1, byte z2, byte z3, byte z4, byte amp = 0)
	{
		_z0 = z0; _z1 = z1; _z2 = z2; _z3 = z3; _z4 = z4; _amp = amp;
	}

	/// <summary>Os membros arrancados, em bits. Ver <see cref="Membro"/>.</summary>
	public Membro Amputados => (Membro)_amp;

	public byte AmputadosBruto => _amp;

	public bool Perdeu(Membro m) => (_amp & (byte)m) != 0;

	public byte Bruto(int zona) => zona switch
	{
		0 => _z0, 1 => _z1, 2 => _z2, 3 => _z3, _ => _z4,
	};

	/// <summary>Hematoma da zona, de 0 a 1.</summary>
	public float Hematoma(ZonaDeFerida z) => (Bruto((int)z) >> 4) / (float)Degraus;

	/// <summary>Sangue da zona, de 0 a 1.</summary>
	public float Sangue(ZonaDeFerida z) => (Bruto((int)z) & 0x0F) / (float)Degraus;

	public bool Limpa => (_z0 | _z1 | _z2 | _z3 | _z4 | _amp) == 0;

	public bool Equals(MascaraDeFeridas o) =>
		_z0 == o._z0 && _z1 == o._z1 && _z2 == o._z2 && _z3 == o._z3 && _z4 == o._z4 && _amp == o._amp;

	public override bool Equals(object? o) => o is MascaraDeFeridas m && Equals(m);
	public override int GetHashCode() => HashCode.Combine(_z0, _z1, _z2, _z3, _z4, _amp);
	public static bool operator ==(MascaraDeFeridas a, MascaraDeFeridas b) => a.Equals(b);
	public static bool operator !=(MascaraDeFeridas a, MascaraDeFeridas b) => !a.Equals(b);

	public override string ToString() =>
		$"cab {_z0:X2} tor {_z1:X2} abd {_z2:X2} bra {_z3:X2} per {_z4:X2} amp {_amp:X1}";
}

/// <summary>
/// DE QUANTO DOEU PRA COMO SE VE.
///
/// ============================ A CURVA, E QUEM A ESCOLHEU ============================
/// O dono foi explicito: "o sangue so apareceria se o membro estiver muito ferido, ele comeca
/// com o roxo e depois vira o sangue". Entao as duas camadas NAO sobem juntas:
///
///   dano 0%  ..  45%   -> so HEMATOMA, subindo ate encher
///   dano 55% .. 90%    -> o SANGUE entra por cima e vai enchendo
///   decepado           -> as duas no maximo
///
/// A folga entre 45% e 55% e proposital: e o trecho em que o roxo ja esta cheio e o sangue
/// ainda nao comecou. Sem ela as duas camadas subiriam praticamente juntas e o roxo nunca
/// seria uma FASE -- seria so um tom mais escuro do vermelho.
///
/// O limiar de QUEBRA do jogo e 20% de vida, ou seja 80% de dano: nessa hora o sangue esta em
/// ~71%. Um membro quebrado sangra muito, e nao "comeca a sangrar" -- que e o que se espera de
/// ver um osso ceder.
/// ====================================================================================
/// </summary>
public static class Feridas
{
	/// <summary>
	/// A ZONA MORTA: abaixo disto nao aparece nada.
	///
	/// Sem ela o primeiro arranhao ja manchava o corpo -- e como um membro fica abaixo de 100%
	/// quase o tempo todo numa sessao de luta, todo mundo andaria permanentemente pintado. Marca
	/// que nunca sai deixa de ser marca.
	/// </summary>
	private const double HematomaComeca = 0.15;

	/// <summary>Onde o hematoma enche.</summary>
	private const double HematomaCheio = 0.50;

	/// <summary>Onde o sangue comeca e onde ele enche.</summary>
	private const double SangueComeca = 0.55, SangueCheio = 0.90;

	/// <summary>
	/// A mascara de um corpo.
	///
	/// A ZONA FICA COM O PIOR DOS MEMBROS DELA, e nao com a media: um braco quebrado e um braco
	/// inteiro nao sao "um braco meio quebrado". A media apagaria justamente o membro que o
	/// jogador mirou.
	/// </summary>
	public static MascaraDeFeridas De(Body corpo)
	{
		Span<byte> z = stackalloc byte[MascaraDeFeridas.Zonas];
		byte amp = 0;

		foreach (BodyPart p in corpo.Partes)
		{
			// ARRANCADO SOME. A MAO leva o braco junto e o PE leva a perna: no sprite de 32 px o
			// braco inteiro tem uns quatro pixels de largura, entao "so a mao" nao e uma regiao
			// que exista pra desenhar. Perder a mao ja e perder o braco, visualmente.
			if (p.Decepado) amp |= (byte)(p.Nome switch
			{
				"Braco esquerdo" or "Mao esquerda" => MascaraDeFeridas.Membro.BracoEsq,
				"Braco direito" or "Mao direita" => MascaraDeFeridas.Membro.BracoDir,
				"Perna esquerda" or "Pe esquerdo" => MascaraDeFeridas.Membro.PernaEsq,
				"Perna direita" or "Pe direito" => MascaraDeFeridas.Membro.PernaDir,
				_ => MascaraDeFeridas.Membro.Nenhum,   // nucleo arrancado MATA: nao ha corpo andando
			});

			if (Zona(p.Zona) is not { } zi) continue;

			// DECEPADO E O MAXIMO. Nao ha "fracao" de um membro que nao existe mais -- e o
			// `Fracao` de um decepado continua sendo o que ele tinha quando saiu, que enganaria.
			double dano = p.Decepado ? 1.0 : 1.0 - p.Fracao;

			int hema = Passos((dano - HematomaComeca) / (HematomaCheio - HematomaComeca));
			int sang = Passos((dano - SangueComeca) / (SangueCheio - SangueComeca));

			byte atual = z[(int)zi];
			int hAtual = atual >> 4, sAtual = atual & 0x0F;
			z[(int)zi] = (byte)((Math.Max(hema, hAtual) << 4) | Math.Max(sang, sAtual));
		}

		return new MascaraDeFeridas(z[0], z[1], z[2], z[3], z[4], amp);
	}

	private static int Passos(double t) =>
		(int)Math.Round(Math.Clamp(t, 0, 1) * MascaraDeFeridas.Degraus);

	/// <summary>
	/// ============================ O DEGRAU EM QUE A FERIDA VIRA **GRAVE** ============================
	/// **NAO E UM NUMERO SOLTO.** Ele e o limiar de QUEBRA do jogo (`Regras.LimiarQuebra`, 20% de vida
	/// = 80% de dano) passado pela MESMA curva de sangue que pinta o sprite -- ou seja, e o degrau em
	/// que o cabecalho desta classe ja dizia que o corpo esta: *"o limiar de QUEBRA do jogo e 20% de
	/// vida, ou seja 80% de dano: nessa hora o sangue esta em ~71%"*. Hoje isso da **11 de 15**.
	///
	/// Por que derivar em vez de cravar 11: se alguem afinar a curva do sangue (`SangueComeca` /
	/// `SangueCheio`) ou o limiar de quebra, um 11 cravado passaria a querer dizer outra coisa
	/// caladamente -- e "grave" deixaria de ser "esse membro esta cedendo" pra virar "esse membro esta
	/// um pouco roxo". Derivado, a frase continua sendo a mesma frase.
	///
	/// **E POR QUE O SANGUE E NAO O HEMATOMA**: o hematoma ENCHE aos 50% de dano (`HematomaCheio`), e
	/// meio corpo de qualquer sessao de luta anda com ele cheio. Uma regra pendurada nele dispararia
	/// em briga de rua. O sangue so comeca aos 55% e so enche aos 90% -- ele e a camada que separa
	/// "apanhou" de "esta se desfazendo".
	/// ============================================================================================
	/// </summary>
	public static readonly int DegrauGrave =
		Passos((1.0 - Regras.LimiarQuebra - SangueComeca) / (SangueCheio - SangueComeca));

	/// <summary>
	/// ESTE CORPO ESTA COM FERIMENTO **GRAVE**? -- lido da mascara que ja existe, e de mais nada.
	///
	/// ============================ POR QUE A MASCARA E NAO A VIDA ============================
	/// `Body.Vida()` e a MEDIA das partes, e o cabecalho do <see cref="MascaraDeFeridas"/> ja explica
	/// o que a media apaga: *"um lutador com 70% de vida porque levou tudo na perna e um lutador com
	/// 70% espalhado sao a mesma barra e dois desenhos completamente diferentes"*. Um Saiyajin com a
	/// perna aberta e o abdomen rasgado pode ter 60% de media -- e quem olha pra ele ve um homem se
	/// esvaindo. A media diria "esta bem".
	///
	/// A mascara ja e o PIOR dos membros de cada regiao (ver <see cref="De"/>), que e exatamente a
	/// pergunta que "ferimento grave" faz.
	///
	/// **E ELA JA ESTA CALCULADA**: o servidor recalcula a mascara de todo corpo a 5 Hz e guarda em
	/// `ServerPlayer.EnvFeridas` pra mandar pra zona (`GameServer.Feridas.cs`). Perguntar aqui custa
	/// cinco leituras de byte -- nao ha um `Feridas.De` novo por corpo por tique.
	/// ====================================================================================
	///
	/// MEMBRO ARRANCADO E GRAVE POR DEFINICAO, sem passar pela curva: o <see cref="De"/> ja poe as
	/// duas camadas no maximo pra ele, mas um braco decepado que REGENEROU ate a vida cheia (Namek,
	/// Majin) sairia da curva e continuaria faltando -- e faltar um braco nunca deixa de ser grave.
	/// </summary>
	public static bool Grave(MascaraDeFeridas m)
	{
		if (m.Amputados != MascaraDeFeridas.Membro.Nenhum) return true;

		for (int z = 0; z < MascaraDeFeridas.Zonas; z++)
			if ((m.Bruto(z) & 0x0F) >= DegrauGrave) return true;

		return false;
	}

	/// <summary>
	/// O texto do campo <see cref="BodyPart.Zona"/> virando enum.
	///
	/// Nulo pra zona que o sprite nao sabe desenhar. Hoje isso nao acontece -- as cinco cobrem
	/// todos os membros do humanoide --, mas o rabo entra em "abdomen" e um corpo de outra
	/// forma (bicho, androide) pode trazer nome novo. Devolver nulo e ignorar e melhor que
	/// pintar sangue no lugar errado.
	/// </summary>
	public static ZonaDeFerida? Zona(string zona) => zona switch
	{
		"cabeca" => ZonaDeFerida.Cabeca,
		"torso" => ZonaDeFerida.Torso,
		"abdomen" => ZonaDeFerida.Abdomen,
		"bracos" => ZonaDeFerida.Bracos,
		"pernas" => ZonaDeFerida.Pernas,
		_ => null,
	};
}
