namespace Jandirus.Core.Combat;

/// <summary>
/// O SOM DE UM ATAQUE DE KI -- qual arquivo o original toca quando a tecnica SAI, e qual toca enquanto ela CARREGA.
///
/// ============================ O QUE ESTAVA ERRADO (dono, 2026-10-13) ============================
/// *"o som ao soltar ataques de ki ta errado (veja no DM qual era o som) e tb tem o som do ataque andando em si q
/// tb tem no DM"*. Todo tiro nascia com o baque de SOCO (`Trilha.Acerto(1)`), fosse uma bola de dedo ou um
/// Kamehameha; nenhum dos arquivos de ki do original era tocado, embora todos ja estivessem importados.
///
/// No DM cada verb toca o dele (`usr.emit_Sound(...)`), e sao poucos arquivos:
///   * a bola: `fire_kiblast.wav` (`blasts.dm:58,108`, `Spirit.dm:359`, a lamina do Kiai `Kiai.dm:37`);
///   * as rajadas e as bolas que nascem em volta do alvo: `KIBLAST.WAV` (`blasts.dm:164,227,290,360,434,497`);
///   * as que queimam: `burning_fire.wav` (`blasts.dm:561`, `GuidedBall.dm:77`, `BusterShell.dm:42`);
///   * os discos: `disc_fire.wav` (`discs.dm:52`, `KillDriver.dm:44`); o Kikoho: `kikoho.wav` (`Kikoho.dm:31`);
///   * a Death Ball ao partir: `Blast.wav` (`DeathBall.dm:87`); a Genkidama: `spiritbombfire.wav` (`SpiritBomb.dm:173`);
///   * TODO RAIO: `kamehameha_fire.wav` ao soltar e `kame_charge.wav` ao carregar (`beams.dm:283,290` e as irmas),
///     menos o Final Flash, que usa `basicbeam_fire.wav` e `basicbeam_chargeoriginal.wav` (`FinalFlash.dm:38,41`).
///
/// "O SOM DO ATAQUE ANDANDO" e o do raio: `kamehameha_fire.wav` dura 5,4 s -- o tempo que o feixe passa no ar --, e
/// no cliente ele fica preso NA CABECA do raio e acaba com ela (`World.AoNascerTiro`). O id viaja no pacote de
/// nascimento do tiro, um byte: o cliente nao sabe qual verb atirou, so o tipo e a arte.
/// =================================================================================================
/// </summary>
public enum SomDeKi : byte
{
	/// <summary>Sem som proprio (o tiro de bancada, o que nasce calado).</summary>
	Nenhum = 0,

	/// <summary>`fire_kiblast.wav` -- a bola de ki.</summary>
	Bola = 1,

	/// <summary>`KIBLAST.WAV` -- as rajadas e as bolas que nascem em volta do alvo.</summary>
	Rajada = 2,

	/// <summary>`burning_fire.wav` -- a esfera teleguiada, a Buster Shell, a Scattering Bullet.</summary>
	Queima = 3,

	/// <summary>`disc_fire.wav` -- o Kienzan e o Kill Driver.</summary>
	Disco = 4,

	/// <summary>`kikoho.wav`.</summary>
	Kikoho = 5,

	/// <summary>`Blast.wav` -- a Death Ball partindo.</summary>
	EsferaDaMorte = 6,

	/// <summary>`spiritbombfire.wav` -- a Genkidama partindo.</summary>
	Genkidama = 7,

	/// <summary>`kamehameha_fire.wav` -- todo raio, e ele ANDA com a cabeca.</summary>
	Raio = 8,

	/// <summary>`basicbeam_fire.wav` -- o Final Flash.</summary>
	RaioBasico = 9,

	// ---- (v0.2.1) as armas de fogo: `shotsound`, tocado pelo `Shoot()` (`Tech/Guns.dm:120`) ----
	/// <summary>`handgunheavy.ogg` -- a pistola (`Guns.dm:246`).</summary>
	Pistola = 10,

	/// <summary>`riflelight.ogg` -- o `shotsound` de fabrica: a metralhadora e o rifle (`Guns.dm:86,260`).</summary>
	Rajada7mm = 11,

	/// <summary>`shotgun.ogg` -- a escopeta (`Guns.dm:271`).</summary>
	Escopeta = 12,

	/// <summary>`RPGshot.ogg` -- o lanca-foguetes (`Guns.dm:347`).</summary>
	Foguete = 13,

	/// <summary>`rifleheavy.ogg` -- o Blaster e o Gaster Blaster (`Guns.dm:360,393`); aqui tambem o Bolter e o atordoador, que la saem mudos.</summary>
	Blaster = 14,
}

public static class SonsDeKi
{
	/// <summary>
	/// DEFEITO INJETADO (bancadas `--v021teste` e `--diagv021`): todo tiro volta a nascer sem som de ki nenhum (o cliente caia no
	/// baque de soco). Lido pela propria tabela. Falso em jogo, sempre.
	/// </summary>
	public static bool TiroSemSomDeTeste;

	/// <summary>O som com que esta tecnica SAI. O verb vazio (a bancada, o tiro sem tecnica) responde pelo tipo.</summary>
	public static SomDeKi DoDisparo(string verbo, TipoDeProjetil tipo)
	{
		if (TiroSemSomDeTeste) return SomDeKi.Nenhum;
		return verbo switch
		{
			"Final_Flash" => SomDeKi.RaioBasico,
			"Scattershot" or "Energy_Barrage" or "Continuous_Energy_Bullets" or "Spin_Blast"
				or "Ki_Bomb" or "Hellzone_Grenade" => SomDeKi.Rajada,
			"Guided_Ball" or "BusterShell" or "Scattering_Bullet" => SomDeKi.Queima,
			"Kienzan" or "KillDriver" => SomDeKi.Disco,
			"Kikoho" => SomDeKi.Kikoho,
			"Death_Ball" => SomDeKi.EsferaDaMorte,
			"SpiritBomb" => SomDeKi.Genkidama,
			// A LAMINA DE AR ja soa pelo gesto do sopro (`S2C.Gesto`, `Kiai.dm:37`) e o alvo de treino nasce calado.
			"Kiai" or "Ki_Targets" => SomDeKi.Nenhum,
			// AS ARMAS DE FOGO (v0.2.1) -- a chave e a do `ArteDeProjetil`: o prefixo mais o id da arma.
			Tech.ArmasDeFogo.PrefixoDeVerbo + Tech.ArmasDeFogo.Pistola => SomDeKi.Pistola,
			Tech.ArmasDeFogo.PrefixoDeVerbo + Tech.ArmasDeFogo.Metralhadora or Tech.ArmasDeFogo.PrefixoDeVerbo + Tech.ArmasDeFogo.Rifle => SomDeKi.Rajada7mm,
			Tech.ArmasDeFogo.PrefixoDeVerbo + Tech.ArmasDeFogo.Escopeta => SomDeKi.Escopeta,
			Tech.ArmasDeFogo.PrefixoDeVerbo + Tech.ArmasDeFogo.LancaFoguetes => SomDeKi.Foguete,
			Tech.ArmasDeFogo.PrefixoDeVerbo + Tech.ArmasDeFogo.Blaster or Tech.ArmasDeFogo.PrefixoDeVerbo + Tech.ArmasDeFogo.Gaster
				or Tech.ArmasDeFogo.PrefixoDeVerbo + Tech.ArmasDeFogo.Bolter or Tech.ArmasDeFogo.PrefixoDeVerbo + Tech.Atordoador.Id => SomDeKi.Blaster,
			_ => tipo switch
			{
				TipoDeProjetil.Beam => SomDeKi.Raio,
				TipoDeProjetil.Guided => SomDeKi.Queima,
				_ => SomDeKi.Bola,
			},
		};
	}

	/// <summary>
	/// ESTE SOM ANDA COM O ATAQUE (fica preso no tiro e acaba com ele)? So os dois de raio, que duram o voo inteiro;
	/// os outros sao o estalo da saida e ficam em quem atirou.
	/// </summary>
	public static bool AndaComOAtaque(SomDeKi som) => som is SomDeKi.Raio or SomDeKi.RaioBasico;
}
