using Godot;
using Jandirus.Core.Forms;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// AS CENAS DO DIRETOR (`--trailer <cena>`). O palco, a camera e o relogio moram em `RoboDoTrailer.cs`.
///
/// O `--trailerarg` e uma lista `chave=valor;chave=valor` -- cada cena le so as chaves dela, e todas
/// tem padrao: `lugar=Zona,cx,cy` troca o palco, `zoom=N` a lente, e o resto e da cena.
/// </summary>
public partial class RoboDoTrailer
{
	private Dictionary<string, Func<IEnumerable<double>>> Cenas => new()
	{
		["batedor"] = Batedor,
		["figurino"] = Figurino,
		["luzes"] = Luzes,
		["voo"] = Voo,
		["formas"] = Formas,
		["ki"] = Ki,
		["duelo"] = Duelo,
		["embate"] = Embate,
		["chefe"] = Chefe,
		["torneio"] = Torneio,
		["fusao"] = Fusao,
		["esferas"] = EsferasDoDragao,
		["alem"] = OutroMundo,
		["morte"] = Morte,
		["espaco"] = EspacoSideral,
		["climas"] = Climas,
		["dia"] = DiaENoite,
		["palco"] = Palco,
		["menus"] = Menus,
		["cidade"] = Cidade,
	};

	private IEnumerable<double>? Escolher() => Cenas.TryGetValue(Cena, out Func<IEnumerable<double>>? f) ? f() : null;

	// =====================================================================
	// O QUE TODA CENA USA
	// =====================================================================
	/// <summary>Uma chave do `--trailerarg` (`chave=valor;...`), ou o padrao.</summary>
	private string Opt(string chave, string padrao)
	{
		foreach (string par in Arg.Split(';', StringSplitOptions.RemoveEmptyEntries))
		{
			int i = par.IndexOf('=');
			if (i > 0 && par[..i].Trim() == chave) return par[(i + 1)..].Trim();
		}
		return padrao;
	}

	private double OptN(string chave, double padrao) =>
		double.TryParse(Opt(chave, ""), System.Globalization.NumberStyles.Float,
						System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : padrao;

	/// <summary>O palco da cena: `lugar=Zona,cx,cy`, ou o padrao que a cena der.</summary>
	private (string Zona, float Cx, float Cy) Lugar(string padrao)
	{
		string[] p = Opt("lugar", padrao).Split(',');
		return (p[0], float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture),
				float.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture));
	}

	/// <summary>
	/// A hora e o tempo da zona em que o corpo esta. Meio-dia de ceu limpo, se ninguem pedir outra coisa.
	///
	/// A FORCA PADRAO E BAIXA DE PROPOSITO: no `ForcarClima` o mais forte vence, e um ceu limpo de forca
	/// 1 calaria a tempestade que a propria transformacao abre (`ClimaPorTransformacao`) -- a primeira
	/// tomada do SSJ1 saiu com sol a pino do comeco ao fim por causa disso. O ceu limpo do diretor e so
	/// o estado de REPOUSO da cena; o que o jogo quiser por em cima, entra.
	/// </summary>
	private void Luz(double hora = Ceu.MeioDia, TipoDeClima clima = TipoDeClima.Limpo, double forca = 0.02)
	{
		S?.CeuDoTrailer(_eu, hora, clima, forca);
		if (hora >= 0) _horaCravada = hora;
	}

	/// <summary>O figurino do heroi: `cabelo=` e `roupa=a+b+c` trocam o padrao.</summary>
	private void VestirOHeroi()
	{
		List<string> faltou = S?.VestirNoTrailer(_eu, Opt("cabelo", "Goku"),
			Opt("roupa", "Clothes_TurtleSuit").Split('+', StringSplitOptions.RemoveEmptyEntries)) ?? [];
		if (faltou.Count > 0) Nota("FIGURINO: nao achei " + string.Join(", ", faltou));
	}

	/// <summary>
	/// O CORPO LOCAL SOME DA TELA (e so da tela: no servidor ele continua onde esta). E pras cenas em
	/// que o heroi e so o par de olhos -- o duelo de dois NPCs, a luta de chefe. O servidor so manda a
	/// quem esta perto os corpos que estao perto (o campo de visao), entao a camera nao pode ir
	/// sozinha: o corpo tem que estar na cena, e o jeito de ele nao aparecer nela e nao ser desenhado.
	/// </summary>
	private void EsconderOHeroi(bool esconder)
	{
		if (M?.CorpoDeTeste(_eu) is { } corpo) corpo.Visible = !esconder;
	}

	private int _dupla1, _dupla2;

	/// <summary>A camera no MEIO de dois corpos (o enquadramento do espectador do torneio, pra dois quaisquer).</summary>
	private void SeguirDois(int a, int b)
	{
		_panando = false;
		_seguindo = 0;
		_dupla1 = a; _dupla2 = b;

		// `lente=auto` (o padrao das lutas): a lente segue a distancia dos dois. Um numero crava.
		string lente = Opt("zoom", "auto");
		_lenteDaDupla = a != 0 && b != 0 && lente == "auto" ? 3 : 0;
		if (_lenteDaDupla > 0) Zoom(_lenteDaDupla);
	}

	// =====================================================================
	// O RECONHECIMENTO -- onde filmar, e com que roupa
	// =====================================================================
	/// <summary>
	/// O BATEDOR: vai a cada ponto de uma lista (`Zona,cx,cy[,zoom]`, uma por linha no arquivo do
	/// `--trailerarg`) e tira uma foto de la, ao meio-dia e sem HUD. Nao e cena do trailer: e como o
	/// diretor escolhe os lugares sem adivinhar pelo `.col`, que so sabe onde ha parede.
	/// </summary>
	private IEnumerable<double> Batedor()
	{
		Tela(hud: false, chat: false);
		if (!System.IO.File.Exists(Arg)) { Nota($"o batedor quer um arquivo de pontos em --trailerarg (recebi `{Arg}`)"); yield break; }

		foreach (string cru in System.IO.File.ReadAllLines(Arg))
		{
			string[] p = cru.Trim().Split(',');
			if (p.Length < 3 || !float.TryParse(p[1], out float cx) || !float.TryParse(p[2], out float cy)) continue;
			int zoom = p.Length > 3 && int.TryParse(p[3], out int z) ? z : 1;

			foreach (double s in Viajar(p[0], cx, cy)) yield return s;
			Luz();
			Zoom(zoom);
			yield return 1.6;
			Foto($"bat-{p[0]}-{cx:0}-{cy:0}-z{zoom}");
			Marca($"foto de {p[0]} ({cx:0},{cy:0}) zoom {zoom}");
			yield return 0.2;
		}
	}

	/// <summary>
	/// O FIGURINO: veste o corpo com cada combinacao candidata e fotografa de perto, pra o diretor
	/// escolher a roupa do heroi olhando a roupa -- e nao pelo nome do arquivo.
	/// </summary>
	private IEnumerable<double> Figurino()
	{
		Tela(hud: false, chat: false);
		(string zona, float cx, float cy) = Lugar("Earth,220,240");
		foreach (double s in Viajar(zona, cx, cy)) yield return s;
		Luz();
		Zoom(5);
		yield return 1.0;

		(string Cabelo, string[] Pecas)[] provas =
		[
			("Goku", ["Clothes_TurtleSuit"]),
			("Goku", ["Clothes_GiTop", "Clothes_GiBottom", "Clothes_Wristband", "Clothes_Boots"]),
			("Goku", ["Clothes_GokuDBSSuit"]),
			("Goku", ["Clothes_GokuJacketandPants"]),
			("Vegeta", ["Clothes_VegetaSaiyanSagaArmor"]),
			("Vegeta", ["Clothes_VegetaDBSArmor"]),
			("Vegeta", ["Clothes_SaiyanSuit", "Armor_Elite", "Clothes, Saiyan Gloves", "Clothes, Saiyan Shoes"]),
			("Future Gohan", ["Clothes_GohanGi"]),
			("Teen Gohan", ["ItemPiccoloCape", "Clothes_GiTop", "Clothes_GiBottom"]),
			("GT Trunks", ["Clothes_Jacket", "Clothes_Pants", "Sword_Trunks"]),
			("Broly", ["BrolyWaistrobe", "Clothes_Wristband"]),
			("Raditz", ["RaditzArmorTobiUchiha"]),
		];

		int n = 0;
		foreach ((string cabelo, string[] pecas) in provas)
		{
			List<string> faltou = S?.VestirNoTrailer(_eu, cabelo, pecas) ?? [];
			yield return 1.2;
			Foto($"figurino-{n:00}");
			Marca($"figurino {n:00}: {cabelo} + {string.Join(" + ", pecas)}" + (faltou.Count > 0 ? $" -- FALTOU {string.Join(", ", faltou)}" : ""));
			n++;
		}
	}

	/// <summary>
	/// AS LUZES: o mesmo corpo, carregando Ki, em cada hora do dia e debaixo de cada clima -- uma foto de
	/// cada. E como o diretor escolhe a hora de cada cena: a luz de ki so acende de noite
	/// (`Client/LuzDeKi.cs`), e ao meio-dia uma aura dourada some em cima da grama clara.
	/// </summary>
	private IEnumerable<double> Luzes()
	{
		Tela(hud: false, chat: false);
		(string zona, float cx, float cy) = Lugar("Earth,200,236");
		foreach (double s in Viajar(zona, cx, cy)) yield return s;
		VestirOHeroi();
		Zoom((int)OptN("zoom", 4));
		string forma = Opt("forma", "ssj1");
		if (forma.Length > 0) { S?.FormaNoTrailer(_eu, forma); yield return 32; }
		bool carrega = Opt("carrega", "1") == "1";

		foreach (string texto in Opt("horas", "0.5,0.62,0.70,0.74,0.78,0.82,0.90,0.0,0.22,0.27").Split(','))
		{
			double hora = double.Parse(texto, System.Globalization.CultureInfo.InvariantCulture);
			Luz(hora, TipoDeClima.Limpo, 0.05);
			S?.RegarOKiDeFoto(_eu);
			yield return 1.5;
			if (carrega) C?.SendCarregar(true);
			yield return 1.6;
			Foto($"luz-hora-{hora:0.000}".Replace(',', '.'));
			Marca($"luz: hora {hora:0.000}");
			C?.SendCarregar(false);
			yield return 0.5;
		}

		if (Opt("climas", "1") != "1") yield break;
		foreach (TipoDeClima clima in Enum.GetValues<TipoDeClima>())
		{
			if (clima == TipoDeClima.Limpo) continue;
			Luz(Ceu.MeioDia, clima, 1);
			yield return 5.0;
			Foto($"luz-clima-{(int)clima:00}-{clima}");
			Marca($"luz: clima {clima}");
		}
	}

	// =====================================================================
	// O VOO -- e o passeio pelos mundos
	// =====================================================================
	/// <summary>
	/// O HEROI VOA POR CIMA DE UMA LISTA DE LUGARES. `rota=Zona,cx,cy,dx,dy,seg|Zona,...`: de onde sai,
	/// pra que lado e por quanto tempo. `altura=` em tiles (a partir de 1 o corpo passa por cima do
	/// cenario e o veu de sombra abre -- `Voo.AlturaQueAtravessa`), `correr=1` segura o superflight.
	///
	/// Voa de verdade, pelo piloto automatico e pelo `AlternarVoo` do servidor: nao e a camera solta
	/// passeando. E a unica maneira de a cidade estar povoada na tela -- o servidor so manda os corpos
	/// que estao no campo de visao de quem olha.
	/// </summary>
	private IEnumerable<double> Voo()
	{
		Tela(hud: Opt("hud", "0") == "1", chat: false);
		float altura = (float)OptN("altura", 3) * T;
		bool correr = Opt("correr", "0") == "1";
		int zoom = (int)OptN("zoom", 3);
		bool vestido = false;

		foreach (string trecho in Opt("rota", "Earth,200,240,1,0,8").Split('|', StringSplitOptions.RemoveEmptyEntries))
		{
			string[] p = trecho.Split(',');
			if (p.Length < 6) { Nota($"trecho torto: `{trecho}`"); continue; }
			float cx = float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture);
			float cy = float.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture);
			var rumo = new Vector2(float.Parse(p[3], System.Globalization.CultureInfo.InvariantCulture),
								   float.Parse(p[4], System.Globalization.CultureInfo.InvariantCulture));
			double seg = double.Parse(p[5], System.Globalization.CultureInfo.InvariantCulture);

			foreach (double s in Viajar(p[0], cx, cy)) yield return s;
			if (!vestido) { VestirOHeroi(); vestido = true; }
			LuzDaCena();
			Zoom(zoom);
			bool subiu = S?.VoarNaBoca(_eu, altura) ?? false;
			yield return 1.2;

			Marca($"voo em {p[0]}: sai de ({cx:0},{cy:0}) rumo ({rumo.X:0.#},{rumo.Y:0.#}) por {seg:0.#}s" + (subiu ? "" : " -- NAO SUBIU"));
			if (correr) Input.ActionPress("run");
			M?.AndarDeTeste(rumo);
			yield return seg;
			M?.PararDeTeste();
			if (correr) Input.ActionRelease("run");
			Marca($"voo em {p[0]}: chegou em ({Aqui.X / T:0},{Aqui.Y / T:0})");
			yield return 0.6;
			S?.PousarNaBoca(_eu);
			yield return 0.4;
		}
	}

	// =====================================================================
	// AS TRANSFORMACOES
	// =====================================================================
	/// <summary>
	/// UMA FORMA DEPOIS DA OUTRA, cada uma com a cinematica de ESTREIA (o personagem e novo, entao toda
	/// forma e a primeira vez dele nela -- ver `Cinematicas.Degrau`). `formas=ssj1,ssj2,...`.
	///
	/// Entre uma e outra o corpo muda de lugar: a cratera da anterior nao pode estar no quadro da
	/// seguinte. E o ceu e limpo de novo, porque a propria transformacao fecha o tempo (`ForcarClima`
	/// pelo `ClimaPorTransformacao`) -- o que e do jogo e fica na cena, mas nao pode vazar pra proxima.
	/// </summary>
	private IEnumerable<double> Formas()
	{
		Tela(hud: false, chat: false);
		(string zona, float cx, float cy) = Lugar("Earth,200,236");
		foreach (double s in Viajar(zona, cx, cy)) yield return s;
		VestirOHeroi();
		Zoom((int)OptN("zoom", 4));
		double pose = OptN("pose", 3);

		// `lua=1`: a noite de LUA CHEIA desta zona, pelo `admin_lua_cheia` de producao (ele PROCURA a
		// proxima no relogio do planeta; nao inventa ceu). E a condicao da fera -- e uma noite bonita.
		bool lua = Opt("lua", "0") == "1";
		if (lua)
		{
			C?.SendVerbo("admin_lua_cheia");
			yield return 1.5;
			// A NOITE ACHADA FICA: a janela da lua cheia tem minutos, e a cena tambem.
			_horaCravada = S?.HoraAgoraNoTrailer(_eu) ?? -1;
			Nota($"lua cheia: a hora ficou cravada em {_horaCravada:0.000}");
		}

		int n = 0;
		foreach (string id in Opt("formas", "ssj1,ssj2,ssj3").Split(',', StringSplitOptions.RemoveEmptyEntries))
		{
			if (Catalogo.Def(id) is not { } def) { Nota($"nao existe a forma `{id}`"); continue; }

			Por(cx + 16 * n, cy, Facing.South);
			n++;
			LuzDaCena(lua ? -1 : 0.80);
			yield return 2.0;

			double cena = Cinematicas.Para(def)?.Segundos ?? 0;
			Marca($"forma {id}: COMECA (cena de {cena:0.0}s)");
			S?.FormaNoTrailer(_eu, id);
			yield return cena + 0.4;

			Marca($"forma {id}: de pe");
			yield return pose;

			C?.SendCarregar(true);
			Marca($"forma {id}: carregando");
			yield return pose;
			C?.SendCarregar(false);
			yield return 0.8;

			S?.FormaNoTrailer(_eu, Catalogo.IdBase);
			yield return 1.2;
		}
	}

	// =====================================================================
	// O ARSENAL DE KI
	// =====================================================================
	/// <summary>As tecnicas do arsenal, na ordem em que o heroi as dispara. `tecnicas=a,b,c` troca a lista.</summary>
	private const string Arsenal =
		"Kamehameha,Final_Flash,GalicGun,Masenko,Makkankosappo,Death_Beam,Dodompa,Massive_Beam,Ki_Wave,Enkumei,Boom_Wave,"
	  + "Basic_Blast,Charged_Shot,Guided_Ball,Kienzan,Scattering_Bullet,Spirit_Gun,Kikoho,KillDriver,BusterShell,Paralysis,"
	  + "Energy_Barrage,Scattershot,Hellzone_Grenade,Ki_Bomb,Death_Ball,SpiritBomb,Continuous_Energy_Bullets,BusterBarrage,"
	  + "Spin_Blast,Shockwave,Explosive_Roar,Solar_Flare,Final_Explosion";

	/// <summary>As que so voam no segundo aperto (crescem na mao antes). Ver <see cref="Ki"/>.</summary>
	private static readonly HashSet<string> Crescem = ["Death_Ball", "SpiritBomb"];

	/// <summary>
	/// O CORPO LOCAL VIRA PRA UM LADO, e volta pro ponto. Quem manda no `Facing` do jogador e o CLIENTE
	/// (ele vai no pacote de estado), entao escrever o do servidor nao adianta: o pacote seguinte o
	/// desfaz -- a primeira tomada do arsenal saiu com todos os raios pro SUL por causa disso. Vira-se
	/// como o jogador vira: um toque de passo naquele rumo.
	/// </summary>
	private IEnumerable<double> Encarar(Vector2 rumo, Vec2 ponto)
	{
		M?.AndarDeTeste(rumo);
		yield return 0.12;
		M?.PararDeTeste();
		yield return 0.1;
		Facing olhar = Mathf.Abs(rumo.X) >= Mathf.Abs(rumo.Y)
			? (rumo.X >= 0 ? Facing.East : Facing.West)
			: (rumo.Y >= 0 ? Facing.South : Facing.North);
		S?.PorNoPontoNaFotoDoBorrao(_eu, ponto, olhar);
		yield return 0.25;
	}

	/// <summary>
	/// O HEROI DISPARA O ARSENAL, uma tecnica de cada vez, num alvo de pe a alguns tiles. Cada tiro sai
	/// pelo botao do jogador (`UsarHabilidade`, ver `GatilhoDaVariedade`); o que a cena arruma e o
	/// livro cheio, o tanque cheio entre um tiro e o outro, e o alvo de volta no lugar.
	/// </summary>
	private IEnumerable<double> Ki()
	{
		Tela(hud: false, chat: false);
		(string zona, float cx, float cy) = Lugar("Earth,200,236");
		foreach (double s in Viajar(zona, cx, cy)) yield return s;
		VestirOHeroi();
		LuzDaCena(0.80);
		Zoom((int)OptN("zoom", 3));

		int tiles = (int)OptN("alvo", 9);
		double espera = OptN("espera", 4.5);
		(int skills, int verbos) = S?.ArmarParaAVariedade(_eu) ?? (0, 0);
		Nota($"armado: {skills} skills, {verbos} verbos");

		int alvo = S?.MarcarAlvoDaVariedade(_eu, Facing.East, tiles) ?? 0;
		Vec2 doAlvo = S?.CorpoDoTrailer(alvo).Pos ?? default;
		Vec2 ancora = P(Aqui);
		double bpDoAlvo = OptN("bpalvo", 3e9);
		Foco(Aqui + new Vector2(tiles * T / 2f, 0));
		yield return 1.0;

		foreach (string verbo in Opt("tecnicas", Arsenal).Split(',', StringSplitOptions.RemoveEmptyEntries))
		{
			S?.LimparOsTirosDaVariedade(_eu);
			S?.RegarOKiDaVariedade(_eu);
			S?.PoderNoTrailer(alvo, bpDoAlvo);
			S?.PorNoPontoNaFotoDoBorrao(alvo, doAlvo, Facing.West);
			foreach (double s in Encarar(Vector2.Right, ancora)) yield return s;
			yield return 0.6;

			string resposta = S?.TecnicaNoTrailer(_eu, verbo) ?? "";
			Marca($"ki {verbo}: GATILHO" + (resposta.Length > 0 ? $" -- o servidor respondeu \"{resposta}\"" : ""));

			// AS QUE CRESCEM NA MAO (a Death Ball e a Genkidama sobem um estagio a cada 1,5 s, ate quatro)
			// so voam no SEGUNDO aperto -- "aperte de novo pra largar a guia".
			if (Crescem.Contains(verbo))
			{
				yield return OptN("crescer", 6.6);
				resposta = S?.TecnicaNoTrailer(_eu, verbo) ?? "";
				Marca($"ki {verbo}: LARGOU" + (resposta.Length > 0 ? $" -- \"{resposta}\"" : ""));
			}
			yield return espera;
		}

		S?.LimparOsTirosDaVariedade(_eu);
		Foco(null);
	}

	// =====================================================================
	// O DUELO -- dois NPCs de verdade, com o cerebro de producao
	// =====================================================================
	/// <summary>
	/// DOIS LUTADORES SE ENFRENTAM, e quem luta sao os cerebros deles: a cena so impoe a presa (o mesmo
	/// `PresaDoRoteiro` do torneio) e iguala o poder pra a luta durar. `a=molde`, `b=molde`,
	/// `bp=`/`bpb=`, `seg=` quanto filmar, `rounds=` quantas vezes levantar os dois e recomecar.
	/// </summary>
	private IEnumerable<double> Duelo()
	{
		Tela(hud: false, chat: false);
		(string zona, float cx, float cy) = Lugar("Earth,200,236");
		foreach (double s in Viajar(zona, cx, cy)) yield return s;
		LuzDaCena();
		Zoom((int)OptN("zoom", 3));
		EsconderOHeroi(true);

		double bp = OptN("bp", 3e6), bpb = OptN("bpb", bp);
		int a = S?.NpcDoTrailer(_eu, Opt("a", "lutador_de_torneio"), new Vec2(-5 * T, 3 * T)) ?? 0;
		int b = S?.NpcDoTrailer(_eu, Opt("b", "lutador_de_torneio"), new Vec2(5 * T, 3 * T)) ?? 0;
		if (a == 0 || b == 0) { Nota("os lutadores nao nasceram"); yield break; }

		if (Opt("vestir", "1") == "1")
		{
			S?.VestirNoTrailer(a, Opt("cabeloa", "Goku"), Opt("roupaa", "Clothes_TurtleSuit").Split('+'));
			S?.VestirNoTrailer(b, Opt("cabelob", "Vegeta"), Opt("roupab", "Clothes_VegetaSaiyanSagaArmor").Split('+'));
		}
		S?.PoderNoTrailer(a, bp);
		S?.PoderNoTrailer(b, bpb);
		SeguirDois(a, b);
		yield return 1.5;

		int rounds = (int)OptN("rounds", 1);
		for (int r = 0; r < rounds; r++)
		{
			S?.PoderNoTrailer(a, bp);
			S?.PoderNoTrailer(b, bpb);
			S?.DueloNoTrailer(a, b);
			Marca($"duelo: round {r + 1} COMECA ({S?.CorpoDoTrailer(a).Nome} x {S?.CorpoDoTrailer(b).Nome})");

			double t = 0, seg = OptN("seg", 45);
			while (t < seg)
			{
				yield return 0.5;
				t += 0.5;
				(bool ea, _, _, bool koA, bool mortoA, _, _) = S?.CorpoDoTrailer(a) ?? default;
				(bool eb, _, _, bool koB, bool mortoB, _, _) = S?.CorpoDoTrailer(b) ?? default;
				if (!ea || !eb || koA || koB || mortoA || mortoB)
				{
					Marca($"duelo: round {r + 1} ACABOU aos {t:0.0}s (A {(koA || mortoA || !ea ? "caiu" : "de pe")}, B {(koB || mortoB || !eb ? "caiu" : "de pe")})");
					yield return 2.5;
					break;
				}
			}
			S?.DueloNoTrailer(a, 0);
			S?.DueloNoTrailer(b, 0);
		}

		SeguirDois(0, 0);
		Foco(null);
		S?.LimparOTrailer();
		EsconderOHeroi(false);
	}
}
