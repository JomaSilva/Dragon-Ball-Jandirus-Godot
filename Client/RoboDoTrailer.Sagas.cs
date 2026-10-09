using Godot;
using Jandirus.Core.Appearance;
using Jandirus.Core.Forms;
using Jandirus.Core.Social;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// AS CENAS DO SEGUNDO TRAILER: as SAGAS. O dono pediu (2026-10-07) as lutas do anime contadas com os
/// sistemas do jogo -- e por isso estas cenas tem ROTEIRO (quem perde primeiro, em que instante o poder
/// sobe), ao contrario das do primeiro trailer, que so punham o palco e olhavam.
///
/// O que acontece em cada instante continua sendo do jogo (o Kaio-ken, a disputa de ki, a transformacao,
/// a morte do planeta); o diretor escolhe a HORA. Ver `Server/GameServer.Trailer2.cs`.
/// </summary>
public partial class RoboDoTrailer
{
	// =====================================================================
	// A SAGA SAIYAJIN: o Kamehameha contra o Galick Ho, virado por um Kaio-ken
	// =====================================================================
	/// <summary>
	/// *"mostre colisao de ki com um personagem com a aparencia do goku e outro do vegeta, porem o do goku
	/// comeca a perder e no meio da colisao ele usa o kaioken x4 e vence"*.
	///
	/// TRES TEMPOS, e nenhum deles e truque de cena:
	///   1. o lado do Goku responde MAL ao QTE de letras (`lento=` segundos por letra) e o encontro anda
	///      pra cima dele;
	///   2. o Kaio-ken acende -- o `Kaioken` de producao: o buff, a aura vermelha, o estouro e o grito;
	///   3. ele passa a responder TODAS as letras, e o encontro volta. Quem empurra e o jogo: a disputa
	///      de ki rele o poder de cada lado a cada tique (`GameServer.LerOPoderDeAgora`), entao o salto
	///      do Kaio-ken entra no cabo de guerra sem ninguem reescrever a vantagem.
	/// `bpa=`/`bpb=` o poder de cada um, `vira=` o medidor em que o Kaio-ken entra, `vezes=` o multiplo.
	/// </summary>
	private IEnumerable<double> EmbateComKaioken()
	{
		Tela(hud: false, chat: false);
		(string zona, float cx, float cy) = Lugar("Earth,200,236");
		foreach (double s in Viajar(zona, cx, cy)) yield return s;
		LuzDaCena(0.80);
		Zoom((int)OptN("zoom", 3));
		EsconderOHeroi(true);
		yield return 1.0;

		(int a, int b) = S?.EmbateDeFoto_Montar(_eu, (int)OptN("tiles", 12), OptN("bpa", 6000), OptN("bpb", 6000),
			tecladoEmA: true, tilesAbaixo: (int)OptN("abaixo", 3), verbo: "Kamehameha", escala: OptN("escala", 1),
			verboDeB: "GalicGun", corDeA: Opt("cora", "40,120,255"), corDeB: Opt("corb", "190,60,255")) ?? (0, 0);
		if (a == 0 || b == 0) { Nota("o embate nao achou corredor de chao perto daqui"); yield break; }

		S?.VestirNoTrailer(a, "Goku", Pecas(Opt("roupaa", "Clothes_TurtleSuit")));
		S?.VestirNoTrailer(b, "Vegeta", Pecas(Opt("roupab", RoupaDoRival)));
		SeguirDois(a, b);
		Marca("kaioken: MONTADO");

		// ---- o encontro
		double t = 0;
		while (t < 12 && !(S?.EmbateDeFoto_Estado().Existe ?? false)) { yield return 0.1; t += 0.1; }
		if (t >= 12) { Nota("as duas cabecas nao se encontraram em 12 s"); yield break; }
		Marca("kaioken: as cabecas se ENCONTRARAM");

		// ---- 1) perdendo: uma letra a cada `lento` segundos (o rival empurra 3,2 por segundo)
		double vira = OptN("vira", 24), lento = OptN("lento", 1.2), desde = 0;
		t = 0;
		while (t < 10)
		{
			var e = S?.EmbateDeFoto_Estado() ?? default;
			if (!e.Existe || e.Medidor <= vira) break;
			yield return 0.1;
			t += 0.1;
			if ((desde += 0.1) >= lento) { desde = 0; S?.EmbateDeFoto_Apertar(); }
		}
		Marca($"kaioken: PERDENDO (medidor {S?.EmbateDeFoto_Estado().Medidor ?? 0:0}, aos {t:0.0}s)");

		// ---- 2) o Kaio-ken, e a vantagem que ele da (lida do jogo, um instante depois: a ficha anda a 5 Hz)
		double vezes = OptN("vezes", 4);
		double antes = S?.EmbateDeFoto_Vantagem() ?? 0;
		S?.KaiokenNoTrailer(a, vezes);
		Marca($"kaioken: KAIOKEN x{vezes:0}");

		// ---- 3) a virada: todas as letras
		bool anotou = false;
		t = 0;
		while (t < 16 && (S?.EmbateDeFoto_Estado().Existe ?? false))
		{
			S?.EmbateDeFoto_Apertar();
			yield return 0.1;
			t += 0.1;
			if (anotou || t < 0.6) continue;
			anotou = true;
			Marca($"kaioken: a VIRADA (vantagem {antes:0.00} -> {S?.EmbateDeFoto_Vantagem() ?? 0:0.00}, "
				  + $"medidor {S?.EmbateDeFoto_Estado().Medidor ?? 0:0})");
		}
		Marca($"kaioken: o embate ACABOU ({t:0.0}s depois do Kaio-ken)");
		yield return OptN("depois", 5);

		S?.EmbateDeFoto_Limpar();
		Foco(null);
		EsconderOHeroi(false);
	}

	// =====================================================================
	// O QUE AS SAGAS DIVIDEM
	// =====================================================================
	/// <summary>
	/// UM ATOR: um NPC de molde (com cerebro e papel -- luta sozinho e deixa cadaver), vestido e com o
	/// poder pedido. Chefe de saga nao se veste (o corpo dele e o do degrau) e recebe o poder pelo campo
	/// do roteiro (`BpsPinados`); os outros, pelo `PoderNoTrailer`.
	/// </summary>
	/// <param name="raca">
	/// A raca do corpo, quando o molde a SORTEIA (o lutador sai humano, saiyajin, namekuseijin ou
	/// frost) -- ver `GameServer.NpcDeRacaNoTrailer`. Vazio = a que o molde der.
	/// </param>
	private int Ator(string molde, Vec2 desloc, string cabelo, string roupa, double bp, int estagio = 0, string raca = "")
	{
		double[]? bps = bp > 0 ? [bp, bp, bp, bp, bp, bp, bp, bp] : null;
		int id = raca.Length > 0 ? S?.NpcDeRacaNoTrailer(_eu, molde, raca, desloc) ?? 0
								 : S?.NpcDoTrailer(_eu, molde, desloc, estagio, bps) ?? 0;
		if (id == 0) { Nota($"o ator `{molde}` nao nasceu"); return 0; }

		if (cabelo.Length > 0 || roupa.Length > 0)
		{
			List<string> faltou = S?.VestirNoTrailer(id, cabelo, Pecas(roupa)) ?? [];
			if (faltou.Count > 0) Nota($"FIGURINO de `{molde}`: nao achei " + string.Join(", ", faltou));
			if (bp > 0) S?.PoderNoTrailer(id, bp);
		}
		return id;
	}

	/// <summary>
	/// O MUNDO DA CENA ESTA MORRENDO, e a agonia fica neste patamar ("segundos que faltam", de 310 a 0)
	/// enquanto as esperas da cena o repetirem -- ver `GameServer.AgoniaNoTrailer`. Negativo = nao ha.
	/// </summary>
	private double _agonia = -1;

	/// <summary>O mundo que morre, pelo nome ("Earth") -- ou vazio pra o mundo em que o heroi esta.</summary>
	private string _mundoDaAgonia = "";

	private void SegurarAAgonia()
	{
		if (_agonia >= 0) S?.AgoniaNoTrailer(_eu, _agonia, _mundoDaAgonia);
	}

	/// <summary>Espera `seg` segundos segurando a agonia do mundo (se houver uma) no patamar da cena.</summary>
	private IEnumerable<double> Esperar(double seg)
	{
		for (double t = 0; t < seg; t += 0.5)
		{
			SegurarAAgonia();
			yield return Math.Min(0.5, seg - t);
		}
	}

	/// <summary>
	/// OS DOIS BRIGAM por `seg` segundos, pelos cerebros deles (a presa imposta e o `tourney_engage`).
	/// Ninguem cai antes da hora: a cena cura os dois a cada dois segundos, porque o que se filma e a
	/// LUTA -- quem vence e do roteiro, e vem depois.
	/// </summary>
	/// <param name="soCorpo">
	/// Troca de golpes: os dois lutam so com o corpo. Sem isto dois lutadores completos passam a briga
	/// trocando raios (o cerebro prefere o ki quando tem), e uma "luta" de saga vira um tiroteio.
	/// </param>
	private IEnumerable<double> Brigar(int a, int b, double seg, string marca, bool soCorpo = false)
	{
		S?.PerfilNoTrailer(a, soCorpo);
		S?.PerfilNoTrailer(b, soCorpo);
		S?.DeixaNoTrailer(a, esperar: false);
		S?.DeixaNoTrailer(b, esperar: false);
		S?.DueloNoTrailer(a, b);
		Marca(marca);
		double cura = 0;
		for (double t = 0; t < seg; t += 0.5)
		{
			yield return 0.5;
			SegurarAAgonia();
			if ((cura += 0.5) < 2.0) continue;
			cura = 0;
			S?.CurarDeTeste(a);
			S?.CurarDeTeste(b);
		}
	}

	/// <summary>
	/// OS DOIS PARAM: a presa de cada um e solta e os dois esperam a deixa (um chefe de saga solto caca
	/// o jogador mais perto -- e o jogador desta cena e a camera). Quem os solta e o <see cref="Brigar"/>.
	/// </summary>
	private void Apartar(int a, int b)
	{
		S?.DueloNoTrailer(a, 0);
		S?.DueloNoTrailer(b, 0);
		S?.DeixaNoTrailer(a, esperar: true);
		S?.DeixaNoTrailer(b, esperar: true);
	}

	/// <summary>
	/// OS DOIS LARGAM O KI E O AR LIMPA: os raios abertos fecham, os tiros soltos esvaziam
	/// (`GameServer.ApagarOKiNoTrailer`), e a cena espera ate nao sobrar nenhum -- um raio de vinte
	/// tiles leva alguns tiques pra ser engolido. Sem ki no ar nao espera nada.
	/// </summary>
	private IEnumerable<double> SemKiNoAr(int a, int b, string nome)
	{
		int antes = S?.KiNoArNoTrailer(a, b) ?? 0;
		bool naMao = (S?.TemCanalNoTrailer(a) ?? false) || (S?.TemCanalNoTrailer(b) ?? false);
		if (antes == 0 && !naMao) yield break;

		S?.ApagarOKiNoTrailer(a, b);
		double t = 0;
		for (; t < 3 && (S?.KiNoArNoTrailer(a, b) ?? 0) > 0; t += 0.1) { yield return 0.1; SegurarAAgonia(); }
		Nota($"{nome}: o ki saiu do ar ({antes} tiros, raio na mao {naMao}) em {t:0.0}s -- sobraram {S?.KiNoArNoTrailer(a, b) ?? 0}");
	}

	/// <summary>
	/// O ZANZO CLASH ENTRE DOIS ATORES, na hora que o roteiro marca. Os dois sao postos de pe e frente a
	/// frente antes (um corpo caido ou no ar do arremesso anterior desfaz o embate no primeiro cruzamento
	/// -- a segunda tomada de Namek teve um Zanzo Clash de um quarto de segundo), e depois dele voltam a
	/// esperar a deixa.
	///
	/// E SEM KI NO AR (<see cref="SemKiNoAr"/>): o embate some com os corpos e os leva de um lado pro
	/// outro, e um raio aberto vai junto, preso na boca do dono -- a tomada de Namek que o dono viu teve
	/// uma Onda de Ki girando em pedacos pela tela durante o Zanzo Clash inteiro.
	/// </summary>
	private IEnumerable<double> Zanzo(int a, int b, string nome)
	{
		Apartar(a, b);
		foreach (double s in SemKiNoAr(a, b, nome)) yield return s;
		S?.DePeNoTrailer(a);
		S?.DePeNoTrailer(b);
		Vec2 meio = ((S?.CorpoDoTrailer(a).Pos ?? default) + (S?.CorpoDoTrailer(b).Pos ?? default)) * 0.5f;
		(bool achou, Vec2 esq, Vec2 dir) = S?.CorredorNoTrailer(_eu, 4, (int)OptN("abaixo", 3)) ?? default;
		if (achou) meio = (esq + dir) * 0.5f;
		S?.PorNoPontoNaFotoDoBorrao(a, meio + new Vec2(-1.5f * T, 0), Facing.East);
		S?.PorNoPontoNaFotoDoBorrao(b, meio + new Vec2(1.5f * T, 0), Facing.West);

		// A LENTE PARA EM 3x ATE O EMBATE ACABAR: a automatica mede a distancia dos dois, e num Zanzo Clash
		// ela muda a cada cruzamento -- a tela daria um degrau de zoom no meio do embate.
		int dupla1 = _dupla1, dupla2 = _dupla2;
		_lenteDaDupla = 0;
		Zoom(3);

		// OS DOIS SE ENCARAM antes de sumir. E tambem o tempo de a camera chegar: ela PERSEGUE o meio dos
		// dois, e com 0,6 s o embate comecava com os dois ainda entrando pela borda da tela.
		foreach (double s in Esperar(OptN("encarar", 1.4))) yield return s;
		S?.DePeNoTrailer(a);
		S?.DePeNoTrailer(b);

		bool foi = S?.ZanzoClashNoTrailer(a, b) ?? false;
		Marca($"{nome}: ZANZO CLASH ({(foi ? "comecou" : "RECUSADO")})");
		double t = 0;
		for (; t < 14 && (S?.EmZanzoNoTrailer(a) ?? false); t += 0.25) { yield return 0.25; SegurarAAgonia(); }
		Marca($"{nome}: o Zanzo Clash ACABOU ({t:0.0}s)");
		yield return 1.2;
		if (dupla1 != 0 || dupla2 != 0) SeguirDois(dupla1, dupla2);
	}

	/// <summary>
	/// A COLISAO DE KI ENTRE DOIS ATORES: a cena acha o corredor, poe os dois de frente e cada um
	/// canaliza o raio dele (`EmbateDeCena_Disparar`). O lado A responde as letras -- mal (`lento`
	/// segundos por letra) ate o medidor cair a `vira`, e todas dali em diante. `vira` negativo = todas
	/// desde o comeco. Quem empurra o encontro e a disputa de producao.
	/// </summary>
	/// <param name="noMeio">
	/// O que a cena faz no instante da virada (a fala do fantasma, o aumento de poder) -- e ela pode
	/// levar o tempo que quiser: enquanto corre, o lado A segue respondendo.
	/// </param>
	private IEnumerable<double> Colidir(int a, int b, string verboA, string verboB, string corA, string corB,
										double vira, string nome, Func<IEnumerable<double>>? noMeio = null,
										double escala = 1, int tiles = 12, double lento = 1.2, double razao = 0,
										double virada = 0)
	{
		(bool achou, Vec2 esq, Vec2 dir) = S?.CorredorNoTrailer(_eu, tiles, (int)OptN("abaixo", 3)) ?? default;
		if (!achou) { Nota($"{nome}: nao ha corredor de chao pra a colisao perto daqui"); yield break; }

		Apartar(a, b);
		SeguirDois(a, b);
		bool sairam = false;
		for (int tentativa = 0; tentativa < 4 && !sairam; tentativa++)
		{
			// DE PE, E NO LUGAR: a briga do plano anterior pode ter deixado um dos dois no chao ou no ar --
			// e com um raio na mao, ou um tiro solto ainda atravessando o corredor
			S?.SoltarOsRaiosNoTrailer(a, b);
			foreach (double s in SemKiNoAr(a, b, nome)) yield return s;
			S?.DePeNoTrailer(a);
			S?.DePeNoTrailer(b);
			S?.PorNoPontoNaFotoDoBorrao(a, esq, Facing.East);
			S?.PorNoPontoNaFotoDoBorrao(b, dir, Facing.West);
			yield return 0.5;
			S?.DePeNoTrailer(a);
			S?.DePeNoTrailer(b);
			// O HEROI SE VIRA PELO CLIENTE (o `Facing` dele viaja no pacote de estado, e escrever o do servidor
			// dura ate o proximo): um passo pro lado do rival, e de volta pra ponta do corredor.
			if (a == _eu) foreach (double s in Encarar(Vector2.Right, esq)) yield return s;
			else S?.PorNoPontoNaFotoDoBorrao(a, esq, Facing.East);
			S?.PorNoPontoNaFotoDoBorrao(b, dir, Facing.West);

			if (S?.EmbateDeCena_Disparar(a, b, verboA, verboB, corA, corB, escala) != true) { Nota($"{nome}: os raios nao sairam"); yield break; }

			// O RAIO SO NASCE QUANDO A CARGA FECHA (um segundo): e ai que se sabe se os DOIS sairam. Conferir so
			// o canal, dois decimos depois, deixou passar uma tomada em que um dos canais fechou no meio da
			// carga -- a cena marcou "os dois disparam" e ficou doze segundos esperando um encontro de um raio so.
			bool OsDois() => (S?.EmbateDeFoto_Estado().Existe ?? false)
							 || ((S?.TemRaioNoTrailer(a) ?? false) && (S?.TemRaioNoTrailer(b) ?? false));
			for (double carga = 0; carga < 2.0 && !OsDois(); carga += 0.1) yield return 0.1;
			sairam = OsDois();
			if (!sairam) Nota($"{nome}: tentativa {tentativa + 1} -- um dos dois nao disparou (A {S?.TemRaioNoTrailer(a)}, B {S?.TemRaioNoTrailer(b)})");
		}
		if (!sairam) { Nota($"{nome}: os dois raios nao sairam juntos em 4 tentativas"); yield break; }
		Marca($"{nome}: os dois DISPARAM ({verboA} x {verboB})");

		double t = 0;
		while (t < 12 && !(S?.EmbateDeFoto_Estado().Existe ?? false)) { yield return 0.1; t += 0.1; }
		if (t >= 12) { Nota($"{nome}: as duas cabecas nao se encontraram em 12 s"); yield break; }
		Marca($"{nome}: as cabecas se ENCONTRARAM");

		// QUEM E MAIS FORTE NESTE ENCONTRO: `razao` e o poder do feixe de B sobre o de A (1 = parelhos).
		// Acima de 1 o lado A perde terreno mesmo respondendo tudo -- e a virada tem que vir de poder.
		if (razao > 0) Nota($"{nome}: razao {razao:0.00} -> vantagem de A {S?.RazaoDoEmbateNoTrailer(razao) ?? 0:0.00}");

		// ---- perdendo
		double desde = 0;
		for (t = 0; vira >= 0 && t < 10; t += 0.1)
		{
			var e = S?.EmbateDeFoto_Estado() ?? default;
			if (!e.Existe || e.Medidor <= vira) break;
			yield return 0.1;
			if ((desde += 0.1) >= lento) { desde = 0; S?.EmbateDeFoto_Apertar(); }
		}
		if (vira >= 0) Marca($"{nome}: PERDENDO (medidor {S?.EmbateDeFoto_Estado().Medidor ?? 0:0})");

		// ---- o que vira o jogo. Enquanto a cena corre (a fala, o grito), o lado A responde TODAS as letras:
		// ele segura a linha, nao vira -- quem vira e o poder que a cena acende no fim dela.
		if (noMeio != null)
			foreach (double s in noMeio())
				for (double falta = s; falta > 1e-6; falta -= 0.1)
				{
					S?.EmbateDeFoto_Apertar();
					yield return Math.Min(0.1, falta);
				}

		// O TAMANHO DA VIRADA (`virada` = a vantagem minima do lado A dali em diante): o que a furia da
		// depende da ficha sorteada do duble, e com pouco a disputa chega aos quinze segundos EMPATADA.
		if (virada > 0) Nota($"{nome}: virada {virada:0.00} -> vantagem de A {S?.ViradaDoEmbateNoTrailer(virada) ?? 0:0.00}");
		Marca($"{nome}: a VIRADA (vantagem {S?.EmbateDeFoto_Vantagem() ?? 0:0.00}, medidor {S?.EmbateDeFoto_Estado().Medidor ?? 0:0})");

		// ---- todas as letras, ate acabar
		for (t = 0; t < 16 && (S?.EmbateDeFoto_Estado().Existe ?? false); t += 0.1)
		{
			S?.EmbateDeFoto_Apertar();
			yield return 0.1;
			if (((int)(t * 10)) % 10 == 0) SegurarAAgonia();
		}
		Marca($"{nome}: o embate ACABOU");
	}

	// =====================================================================
	// A SAGA DO FREEZA: o amigo morto, o primeiro Super Saiyajin, Namek morrendo
	// =====================================================================
	/// <summary>
	/// *"a cena troca pra uma luta com o freeza, ai mostra um npc amigo dele morrendo na frente dele e
	/// ele vira super saiyajin pela primeira vez"*.
	///
	/// O AMIGO E UM NPC DO MUNDO (molde, cerebro, cadaver), e quem o mata e o chefe, pelo cerebro dele:
	/// a presa imposta e o amigo, e o golpe e letal. O heroi assiste -- e ai entram as duas coisas de
	/// producao que fazem o primeiro Super Saiyajin: o LUTO (`AmigoAbatido`, grau extremo, que abre a
	/// janela de furia) e a TECLA (`Transformar`, que com a furia acesa escolhe o degrau que nasce dela).
	/// A estreia e a cinematica do jogo; o chefe fica plantado, assistindo, como no anime.
	/// </summary>
	private IEnumerable<double> OLutoEmNamek()
	{
		Tela(hud: false, chat: false);
		// TERRA FIRME: Namek e 73% mar, e a marca que o primeiro trailer usava (100,347) e AGUA -- o
		// dono viu a cena inteira "no meio do mar". Esta ilha tem uma clareira de 30x16 so de chao
		// (`Trailer/bruto/t2/terra_firme.py`, que le o `.col` e o `.agua` do mapa).
		(string zona, float cx, float cy) = Lugar("Namek,286,277");
		foreach (double s in Viajar(zona, cx, cy)) yield return s;
		LuzDaCena();
		Zoom((int)OptN("zoom", 3));
		VestirOHeroi();
		foreach (double s in Encarar(Vector2.Right, P(Aqui))) yield return s;
		yield return 1.0;

		// O AMIGO E O TIRANO FICAM TRES FILEIRAS ACIMA DO HEROI, e nao na linha dele: o raio do chefe
		// corre pela fileira dos dois, e na primeira tomada o heroi estava atras do amigo -- "o freeza
		// jogou um beam no goku e nao no kuririn". O amigo e HUMANO (o molde sorteia a raca) e espera a
		// deixa de pe: quem morre nesta cena nao revida, encara.
		string nomeDoAmigo = Opt("nomeamigo", "Kuririn");
		int amigo = Ator(Opt("amigo", "lutador_de_torneio"), new Vec2(4 * T, -3 * T), Opt("cabeloamigo", "Bald"),
						 Opt("roupaamigo", "Clothes_TurtleSuit"), OptN("bpamigo", 30000), raca: Opt("racaamigo", "Human"));
		int chefe = Ator(Opt("molde", "freeza_namek"), new Vec2(11 * T, -3 * T), "", "", OptN("bpchefe", 0), (int)OptN("estagio", 4));
		if (amigo == 0 || chefe == 0) yield break;

		S?.DeixaNoTrailer(chefe, esperar: true);
		S?.DeixaNoTrailer(amigo, esperar: true);
		S?.ApontarNaFotoDeCorpos(amigo, Facing.East);
		S?.ApontarNaFotoDeCorpos(chefe, Facing.West);
		S?.LetalNoTrailer(chefe, true);
		Foco(Aqui + new Vector2(5.5f * T, -1.5f * T));
		yield return 2.0;
		S?.FalaNoTrailer(chefe, Opt("falachefe", "Um por um. Começando por ele."));
		Marca("luto: os tres em cena");
		yield return OptN("antes", 3.0);

		// ---- o tirano mata o amigo
		S?.DeixaNoTrailer(chefe, esperar: false);
		S?.DueloNoTrailer(chefe, amigo);
		Marca("luto: o tirano ATACA o amigo");
		double t = 0, teto = OptN("mata", 22);
		bool Caiu() { var c = S?.CorpoDoTrailer(amigo) ?? default; return !c.Existe || c.Morto; }
		for (; t < teto && !Caiu(); t += 0.1) yield return 0.1;
		if (!Caiu()) { S?.MatarNoVelorioDeTeste(amigo); Nota($"o amigo nao morreu em {teto:0}s de luta -- a cena fechou a conta"); }
		S?.DueloNoTrailer(chefe, 0);
		S?.DeixaNoTrailer(chefe, esperar: true);
		Marca($"luto: o AMIGO MORREU aos {t:0.0}s");
		yield return OptN("silencio", 2.5);

		// ---- o luto, e a tecla
		Foco(null);
		Zoom((int)OptN("zoomheroi", 4));
		S?.FalaNoTrailer(_eu, nomeDoAmigo.ToUpperInvariant() + "!!!");
		yield return 2.0;
		bool erupcao = S?.FuriaNoTrailer(_eu, nomeDoAmigo) ?? false;
		Marca($"luto: a FURIA (erupcao {erupcao})");
		yield return OptN("furia", 2.0);

		string forma = S?.SubirDeFormaNoTrailer(_eu) ?? "";
		if (forma != "ssj1") { Nota($"a tecla deixou o heroi em `{forma}` -- a cena veste o Super Saiyajin"); S?.FormaNoTrailer(_eu, "ssj1"); }
		double cena = Catalogo.Def("ssj1") is { } def ? Cinematicas.Para(def)?.Segundos ?? 0 : 0;
		Marca($"luto: SUPER SAIYAJIN -- a estreia (cena de {cena:0.0}s)");
		yield return cena + 0.5;

		Marca("luto: o Super Saiyajin de pe");
		S?.FalaNoTrailer(_eu, Opt("falaheroi", "EU NÃO VOU TE PERDOAR!!"));
		yield return 3.0;
		C?.SendCarregar(true);
		Marca("luto: carregando");
		yield return OptN("pose", 4);
		C?.SendCarregar(false);
		foreach (double s in Encarar(Vector2.Right, P(Aqui))) yield return s;
		SeguirDois(_eu, chefe);
		yield return 3.0;

		SeguirDois(0, 0);
		Foco(null);
		S?.LimparOTrailer();
	}

	/// <summary>
	/// *"ai a luta continua de uma forma bem cinematica com colisao de ki, zanzoclash, o planeta namek
	/// comeca a explodir durante a luta igual no anime"*.
	///
	/// O Super Saiyajin deste plano e um DUBLE: um guerreiro de molde com o figurino do heroi e a forma
	/// ja vestida (a estreia foi o plano anterior). Ele precisa de CEREBRO pra a luta ser luta -- o
	/// heroi de verdade e o par de olhos, escondido. Os dois brigam pelos cerebros, e a cena marca as
	/// horas: o Zanzo Clash, a colisao (o duble comeca perdendo e vira), e a agonia de Namek subindo de
	/// patamar em patamar por baixo de tudo.
	/// </summary>
	private IEnumerable<double> ALutaEmNamek()
	{
		Tela(hud: false, chat: false);
		(string zona, float cx, float cy) = Lugar("Namek,286,277");
		foreach (double s in Viajar(zona, cx, cy)) yield return s;
		LuzDaCena();
		Zoom((int)OptN("zoom", 3));
		EsconderOHeroi(true);

		double bp = OptN("bp", 4e7);
		int heroi = Ator(Opt("duble", "guardiao_saiyajin"), new Vec2(-5 * T, 3 * T), Opt("cabelo", "Goku"), Opt("roupa", "Clothes_TurtleSuit"), bp);
		int chefe = Ator(Opt("molde", "freeza_namek"), new Vec2(5 * T, 3 * T), "", "", OptN("bpchefe", bp * 1.2), (int)OptN("estagio", 4));
		if (heroi == 0 || chefe == 0) yield break;
		Apartar(heroi, chefe);
		S?.FormaJaVistaNoTrailer(heroi, Opt("forma", "ssj1"));
		SeguirDois(heroi, chefe);
		yield return 2.5;

		// 1) corpo a corpo, com o planeta ainda inteiro
		foreach (double s in Brigar(heroi, chefe, OptN("luta1", 14), "namek: a LUTA comeca")) yield return s;

		// o planeta comeca a se partir
		_agonia = OptN("agonia1", 200);
		SegurarAAgonia();
		Marca($"namek: o PLANETA comeca a morrer (faltam {_agonia:0})");
		foreach (double s in Brigar(heroi, chefe, OptN("luta2", 10), "namek: a luta sob o ceu de destruicao")) yield return s;

		// 2) o Zanzo Clash -- que nasce de uma troca de GOLPES, como no jogo (dois socos no mesmo instante),
		// e nao de um tiroteio: o ki sai do ar e os dois brigam na mao antes de sumirem.
		Apartar(heroi, chefe);
		foreach (double s in SemKiNoAr(heroi, chefe, "namek")) yield return s;
		foreach (double s in Brigar(heroi, chefe, OptN("mao", 4), "namek: a troca de golpes na mao", soCorpo: true)) yield return s;
		foreach (double s in Zanzo(heroi, chefe, "namek")) yield return s;

		_agonia = OptN("agonia2", 110);
		foreach (double s in Brigar(heroi, chefe, OptN("luta3", 10), $"namek: mais luta (faltam {_agonia:0})")) yield return s;

		// 3) a colisao: o duble comeca perdendo e vira
		_agonia = OptN("agonia3", 50);
		S?.CurarDeTeste(heroi);
		S?.CurarDeTeste(chefe);
		IEnumerable<double> AFuria()
		{
			bool erupcao = S?.FuriaNoTrailer(heroi, Opt("nomeamigo", "Kuririn")) ?? false;
			S?.FalaNoTrailer(heroi, Opt("falavirada", "ISSO É PELO KURIRIN!!"));
			Marca($"namek: a FURIA no meio da colisao (erupcao {erupcao})");
			yield return 1.0;
		}
		foreach (double s in Colidir(heroi, chefe, Opt("verboa", "Kamehameha"), Opt("verbob", "Death_Beam"),
									 Opt("cora", "40,120,255"), Opt("corb", "210,40,255"), OptN("vira", 26), "namek",
									 AFuria, razao: OptN("razao", 1.15))) yield return s;

		_agonia = OptN("agonia4", 10);
		foreach (double s in Esperar(OptN("depois", 8))) yield return s;
		Marca($"namek: FIM (agonia {S?.EstadoDaAgoniaNoTrailer().Faltam ?? -1:0}s, {S?.EstadoDaAgoniaNoTrailer().Fase})");

		_agonia = -1;
		SeguirDois(0, 0);
		Foco(null);
		S?.EmbateDeFoto_Limpar();
		S?.FecharAAgoniaNoTrailer();
		S?.LimparOTrailer();
		EsconderOHeroi(false);
	}

	/// <summary>
	/// UM ATOR SEM CEREBRO, pra as cenas marcadas golpe a golpe: o corpo forjado das bancadas de foto
	/// (`ForjarCorpoDeFoto`), vestido. `desloc` e a partir do heroi.
	/// </summary>
	private int Figurante(string nome, Vec2 desloc, string cabelo, string roupa, double bp, Facing olhar)
	{
		int id = S?.ForjarCorpoDeFoto(_eu, desloc, nome, bp, comEscada: true) ?? 0;
		if (id == 0) { Nota($"o figurante `{nome}` nao nasceu"); return 0; }
		List<string> faltou = S?.VestirNoTrailer(id, cabelo, Pecas(roupa)) ?? [];
		if (faltou.Count > 0) Nota($"FIGURINO de `{nome}`: nao achei " + string.Join(", ", faltou));
		S?.ApontarNaFotoDeCorpos(id, olhar);
		return id;
	}

	/// <summary>
	/// ESTE CORPO E UM ESPIRITO: meio transparente e, se for o caso, de aureola. E so desenho, e so
	/// deste lado -- o jogo nao tem fantasma. O tom (`Modulate` da raiz do boneco) e o shader do corpo que
	/// obedece; a aureola e a do morto que ja viajou (`CharacterVisual.MostrarAureola`).
	/// </summary>
	private void Espirito(int id, bool aureola)
	{
		if (M?.CorpoDeTeste(id) is not { } corpo) { Nota($"o espirito {id} ainda nao esta na tela"); return; }
		corpo.Modulate = new Color(0.80f, 0.92f, 1f, 0.58f);
		if (!aureola) return;
		C?.ComAureola.Add(id);
		corpo.GetNodeOrNull<CharacterVisual>("Visual")?.MostrarAureola(true);
	}

	// =====================================================================
	// A SAGA DO CELL: o Super Saiyajin 2, os dois Kamehamehas, o pai atras dele
	// =====================================================================
	/// <summary>
	/// *"na luta contra o cell faz um personagem estilo kid gohan, ai o cell vc usa o sprite dele
	/// perfeito e eles lutando, ai o gohan virando ssj2, e a colisao dos dois kamehamehaas e ai aparece
	/// um npc com a aparencia do goku em super saiyajin atras dele falando pra liberar todo o poder e ele
	/// vence o clash matando o cell"*.
	///
	/// O filho e um DUBLE de molde (cerebro, pra a luta ser luta). A estreia do Super Saiyajin 2 e a
	/// cinematica do jogo, tocada num corpo que "esqueceu" que ja a viu. A colisao e a disputa de
	/// producao: o Cell e mais forte, o filho perde terreno -- ate o pai aparecer (um espirito: so
	/// desenho), a furia acender (`AmigoAbatido`, o mesmo gancho do luto) e o poder de AGORA entrar no
	/// feixe. O Cell regenera e nao morre de dano (`RegeneraDecepado`): quem fecha a morte e a cena,
	/// pelo `Morrer` de producao, no instante em que o raio o engole.
	/// </summary>
	private IEnumerable<double> ASagaDoCell()
	{
		Tela(hud: false, chat: false);
		(string zona, float cx, float cy) = Lugar("Earth,243,468");
		foreach (double s in Viajar(zona, cx, cy)) yield return s;
		LuzDaCena(0.66);
		Zoom((int)OptN("zoom", 3));
		EsconderOHeroi(true);

		// O PENTEADO E O "Kid Gohan" E NAO O "Cell Gohan": so o primeiro tem a folha de Super Saiyajin e a
		// de Super Saiyajin 2 (`Hair_KidGohanSSj`, `Hair_KidGohanSSj2`). Com o outro a forma nao acha o
		// que trocar e o cabelo fica preto dentro da aura dourada -- a primeira tomada saiu assim.
		double bp = OptN("bp", 4e8);
		int filho = Ator(Opt("duble", "guardiao_saiyajin"), new Vec2(-5 * T, 3 * T), Opt("cabelo", "Kid Gohan"), Opt("roupa", "Clothes_GohanGi"), bp);
		int cell = Ator("cell", new Vec2(5 * T, 3 * T), "", "", OptN("bpchefe", bp * 5.2), (int)OptN("estagio", 3));
		if (filho == 0 || cell == 0) yield break;
		S?.CorpoDoAtorNoTrailer(cell, (int)OptN("corpo", 3));     // o Perfeito: `BioAndroids.Corpos[3]`
		Apartar(filho, cell);
		S?.FormaJaVistaNoTrailer(filho, "ssj1");
		SeguirDois(filho, cell);

		// OS DOIS DE VOLTA AS MARCAS DO RINGUE, de pe: um soco pesado arremessa, e duas trocas de golpes
		// depois a luta ja saiu do ladrilho e foi parar na agua (a tomada anterior teve a estreia do Super
		// Saiyajin 2 na beira do lago).
		Vec2 marcaDoFilho = S?.CorpoDoTrailer(filho).Pos ?? default, marcaDoCell = S?.CorpoDoTrailer(cell).Pos ?? default;
		void NoRingue()
		{
			Apartar(filho, cell);
			S?.DePeNoTrailer(filho);
			S?.DePeNoTrailer(cell);
			S?.PorNoPontoNaFotoDoBorrao(filho, marcaDoFilho, Facing.East);
			S?.PorNoPontoNaFotoDoBorrao(cell, marcaDoCell, Facing.West);
		}
		yield return 2.5;

		// 1) o Cell domina -- na mao: "eles trocam golpes"
		foreach (double s in Brigar(filho, cell, OptN("luta1", 11), "cell: a LUTA comeca (Super Saiyajin contra o Perfeito)", soCorpo: true)) yield return s;

		// 2) o Super Saiyajin 2
		NoRingue();
		yield return 0.8;
		SeguirDois(0, 0);
		Seguir(filho);
		Zoom((int)OptN("zoomforma", 4));
		S?.EstreiaDeNovoNoTrailer(filho, "ssj2");
		S?.FormaNoTrailer(filho, "ssj2");
		double cena = Catalogo.Def("ssj2") is { } def2 ? Cinematicas.Para(def2)?.Segundos ?? 0 : 0;
		Marca($"cell: SUPER SAIYAJIN 2 -- a estreia (cena de {cena:0.0}s)");
		yield return cena + 0.5;
		Marca("cell: o Super Saiyajin 2 de pe");
		yield return OptN("pose", 2.5);

		Zoom((int)OptN("zoom", 3));
		SeguirDois(filho, cell);
		foreach (double s in Brigar(filho, cell, OptN("luta2", 6), "cell: a luta em Super Saiyajin 2", soCorpo: true)) yield return s;
		NoRingue();
		yield return 0.4;

		// 2b) o Zanzo Clash, e mais uma troca
		foreach (double s in Zanzo(filho, cell, "cell")) yield return s;
		NoRingue();
		yield return 0.4;
		foreach (double s in Brigar(filho, cell, OptN("luta3", 4), "cell: a ultima troca de golpes", soCorpo: true)) yield return s;
		NoRingue();
		yield return 0.4;

		// 3) os dois Kamehamehas, e o pai
		S?.CurarDeTeste(filho);
		S?.CurarDeTeste(cell);
		S?.LetalNoTrailer(filho, true);
		int pai = 0;
		IEnumerable<double> OPai()
		{
			Vec2 doFilho = S?.CorpoDoTrailer(filho).Pos ?? default, meu = S?.CorpoDoTrailer(_eu).Pos ?? default;
			pai = Figurante("Kakarotto", doFilho + new Vec2(-1.4f * T, -0.5f * T) - meu, "Goku", Opt("roupapai", "Clothes_TurtleSuit"), bp, Facing.East);
			if (pai == 0) yield break;
			S?.FormaJaVistaNoTrailer(pai, "ssj1");
			yield return 0.5;
			Espirito(pai, aureola: true);
			Marca("cell: o PAI aparece atras dele");
			yield return 0.9;
			S?.FalaNoTrailer(pai, Opt("falapai", "LIBERE TODO O SEU PODER!!"));
			Marca("cell: a FALA do pai");
			for (double t = 0; t < OptN("fala", 2.8); t += 0.1) yield return 0.1;
			bool erupcao = S?.FuriaNoTrailer(filho, "o pai") ?? false;
			S?.FalaNoTrailer(filho, Opt("falafilho", "AAAAAAAHHHH!!!"));
			Marca($"cell: TODO O PODER (furia {erupcao})");
			yield return 0.5;
		}
		foreach (double s in Colidir(filho, cell, "Kamehameha", "Kamehameha", Opt("cora", "40,120,255"), Opt("corb", "90,230,170"),
									 OptN("vira", 32), "cell", OPai, razao: OptN("razao", 1.15),
									 virada: OptN("virada", 2.0))) yield return s;

		// 4) o raio engole o Cell
		double espera = 0;
		for (; espera < OptN("impacto", 2.2) && !(S?.CorpoDoTrailer(cell).KO ?? true); espera += 0.1) yield return 0.1;
		bool morreu = S?.MatarNoVelorioDeTeste(cell) ?? false;
		Marca($"cell: o CELL MORREU ({morreu}, {espera:0.0}s depois do embate)");
		yield return OptN("depois", 7);

		SeguirDois(0, 0);
		Foco(null);
		S?.EmbateDeFoto_Limpar();
		S?.LimparOTrailer();
		EsconderOHeroi(false);
	}

	// =====================================================================
	// A SAGA DO BOO: o Super Saiyajin 3, o planeta dos Kaioh, a Genkidama
	// =====================================================================
	/// <summary>
	/// *"a luta contra o majin boo usa o sprite da pure form vs o personagem com a aparencia do goku em
	/// ssj 3 lutando no planeta dos kaioh e no final ele joga uma genkidama no majin boo e mata ele"*.
	///
	/// O "planeta dos Kaioh" nao existe no jogo; o palco e uma ilha de grama do `Heaven`, que e o berco
	/// da raca Kai. O heroi assume o Super Saiyajin 3 pela cena CURTA (a estreia tem 140 segundos); a
	/// luta e do duble dele; e a Genkidama e a do jogador -- dois apertos do `SpiritBomb`, com o tanque
	/// reposto enquanto ela cresce (a forma bebe 2,4% de Ki por segundo e a bola cobra 90% na saida). O
	/// Boo regenera tudo e nao morre de dano: quem fecha a morte e a cena, no instante do impacto.
	/// </summary>
	private IEnumerable<double> ASagaDoBoo()
	{
		Tela(hud: false, chat: false);
		// O GRAMADO DAS CEREJEIRAS do `Heaven` (escolhido por foto: `t2/quadros/batedor2.jpg`). A primeira
		// tomada foi numa praca de ladrilho branco, e a Genkidama -- branca -- sumia em cima dela.
		(string zona, float cx, float cy) = Lugar("Heaven,450,345");
		foreach (double s in Viajar(zona, cx, cy)) yield return s;
		LuzDaCena();
		Zoom((int)OptN("zoom", 3));
		VestirOHeroi();
		double bp = OptN("bp", 6e8);
		S?.PoderNoTrailer(_eu, bp);

		int boo = Ator("majin_boo", new Vec2(8 * T, 0), "", "", OptN("bpchefe", bp * 14));
		if (boo == 0) yield break;
		S?.CorpoDoAtorNoTrailer(boo, (int)OptN("corpo", 2), "nenhuma");     // o Kid Buu: o terceiro corpo masculino de Majin
		S?.DeixaNoTrailer(boo, esperar: true);
		S?.ApontarNaFotoDeCorpos(boo, Facing.West);
		foreach (double s in Encarar(Vector2.Right, P(Aqui))) yield return s;
		SeguirDois(_eu, boo);
		yield return 2.5;

		// 1) o Super Saiyajin 3
		Marca("boo: SUPER SAIYAJIN 3 -- a cena curta");
		S?.FormaCurtaNoTrailer(_eu, "ssj3");
		yield return OptN("cena3", 11);
		Marca("boo: o Super Saiyajin 3 de pe");
		S?.RegarOKiDaVariedade(_eu);
		yield return OptN("pose", 2.5);

		// 2) a luta e do duble: o heroi vira os olhos, tres tiles abaixo
		Vec2 palco = S?.CorpoDoTrailer(_eu).Pos ?? default;
		EsconderOHeroi(true);
		Por(cx, cy + 4, Facing.North);
		yield return 0.4;
		int duble = Ator(Opt("duble", "guardiao_saiyajin"), palco - (S?.CorpoDoTrailer(_eu).Pos ?? default), Opt("cabelo", "Goku"), Opt("roupa", "Clothes_TurtleSuit"), bp);
		if (duble == 0) yield break;
		S?.FormaJaVistaNoTrailer(duble, "ssj3");
		SeguirDois(duble, boo);
		foreach (double s in Brigar(duble, boo, OptN("luta", 14), "boo: a LUTA (Super Saiyajin 3 contra a forma pura)")) yield return s;

		// 3) a Genkidama: o heroi de verdade volta, e o Boo fica na linha dela. O KI DA BRIGA SAI DO AR ANTES:
		// o Boo vai ser levado pra ponta do corredor, e com um raio aberto a cauda ia junto (ela e a boca do
		// dono) -- uma tomada teve o raio dele dobrado em curva por cima da Genkidama que nascia.
		Apartar(duble, boo);
		foreach (double s in SemKiNoAr(duble, boo, "boo")) yield return s;
		S?.TirarDoTrailer(duble);

		// A LINHA DA BOLA TEM QUE ESTAR LIVRE: ela acerta num raio de meio tile, e uma cerejeira no meio do
		// caminho a pararia. O corredor e a mesma busca da colisao -- chao de verdade, de ponta a ponta.
		int alcance = (int)OptN("alcance", 11);
		(bool achou, Vec2 esq, Vec2 dir) = S?.CorredorNoTrailer(_eu, alcance, (int)OptN("abaixo", 3)) ?? default;
		Vec2 doHeroi = achou ? esq : Centro(cx - 5, cy), doBoo = achou ? dir : Centro(cx - 5 + alcance, cy);
		if (!achou) Nota("boo: sem corredor livre pra a Genkidama -- vai na linha do palco mesmo");
		S?.PorNoPontoNaFotoDoBorrao(_eu, doHeroi, Facing.East);
		yield return 0.3;
		EsconderOHeroi(false);
		S?.FormaJaVistaNoTrailer(_eu, "ssj3");
		S?.DePeNoTrailer(boo);
		S?.PorNoPontoNaFotoDoBorrao(boo, doBoo, Facing.West);
		foreach (double s in Encarar(Vector2.Right, doHeroi)) yield return s;
		SeguirDois(0, 0);
		Foco(V((doHeroi + doBoo) * 0.5f) + new Vector2(0, -(float)OptN("sobe", 2.5) * T));
		Zoom((int)OptN("zoomgenki", 2));
		(int skills, int verbos) = S?.ArmarParaAVariedade(_eu) ?? (0, 0);
		S?.CorDeKiNoTrailer(_eu, Opt("corgenki", "90,170,255"));
		S?.LetalNoTrailer(_eu, true);
		S?.RegarOKiDaVariedade(_eu);
		yield return 1.0;

		S?.FalaNoTrailer(_eu, Opt("falagenki", "Me emprestem a sua energia!"));
		string r1 = S?.TecnicaNoTrailer(_eu, "SpiritBomb") ?? "";
		Marca($"boo: a GENKIDAMA nasce ({skills} skills){(r1.Length > 0 ? $" -- \"{r1}\"" : "")}");
		for (double t = 0; t < OptN("crescer", 13); t += 0.5) { S?.RegarOKiDaVariedade(_eu); yield return 0.5; }

		S?.RegarOKiDaVariedade(_eu);
		string r2 = S?.TecnicaNoTrailer(_eu, "SpiritBomb") ?? "";
		Marca($"boo: LARGOU{(r2.Length > 0 ? $" -- \"{r2}\"" : "")}");

		// ate ela chegar: sai dois segundos depois do aperto, a dez tiles por segundo
		bool voou = false;
		double ate = 0;
		for (; ate < 9; ate += 0.05)
		{
			var tiro = S?.TiroDaVariedade(_eu) ?? default;
			if (tiro.Achou && tiro.AndouTiles > 0.5) voou = true;
			if (voou && (!tiro.Achou || (tiro.Pos - doBoo).Length < 56)) break;
			yield return 0.05;
		}
		bool morreu = S?.MatarNoVelorioDeTeste(boo) ?? false;
		Marca($"boo: o BOO MORREU ({morreu}, a bola {(voou ? "voou" : "NAO VOOU")}, {ate:0.0}s depois do aperto)");
		yield return OptN("depois", 7);

		Foco(null);
		S?.LimparOsTirosDaVariedade(_eu);
		S?.LimparOTrailer();
	}

	// =====================================================================
	// O EMBATE FINAL: o Instinto Superior contra o Ultra Ego
	// =====================================================================
	/// <summary>
	/// *"no final coloque um npc com a aparencia do goku em ultra instinto vs um npc igual vegeta em
	/// ultra ego, ai eles fazem uma colisao"*.
	///
	/// Dois figurantes (sem cerebro: a cena e marcada), cada um assume a forma dele com a estreia do jogo
	/// -- as duas ao mesmo tempo --, e depois a colisao de producao, com os dois lados respondendo na
	/// mesma cadencia: e um empate, e o empate deste jogo ESTOURA. O corte pro espaco e da montagem.
	/// </summary>
	private IEnumerable<double> OEmbateFinal()
	{
		Tela(hud: false, chat: false);
		(string zona, float cx, float cy) = Lugar("Earth,233,269");
		foreach (double s in Viajar(zona, cx, cy)) yield return s;
		LuzDaCena(0.82);
		Zoom((int)OptN("zoom", 3));
		EsconderOHeroi(true);
		yield return 1.0;

		double bp = OptN("bp", 5e6);
		int a = Figurante("Kakarotto", new Vec2(-5 * T, 3 * T), "Goku", Opt("roupaa", "Clothes_GokuDBSSuit"), bp, Facing.East);
		int b = Figurante("Vegeta", new Vec2(5 * T, 3 * T), "Vegeta", Opt("roupab", "Clothes_VegetaDBSArmor"), bp, Facing.West);
		if (a == 0 || b == 0) yield break;
		SeguirDois(a, b);
		Marca("final: os dois em cena");
		yield return OptN("antes", 3);

		string formaA = Opt("formaa", "ui_perfected"), formaB = Opt("formab", "ultra_ego");
		S?.FormaNoTrailer(a, formaA);
		S?.FormaNoTrailer(b, formaB);
		double cenaA = Catalogo.Def(formaA) is { } da ? Cinematicas.Para(da)?.Segundos ?? 0 : 0;
		double cenaB = Catalogo.Def(formaB) is { } db ? Cinematicas.Para(db)?.Segundos ?? 0 : 0;
		Marca($"final: as duas ESTREIAS ({formaA} {cenaA:0.0}s, {formaB} {cenaB:0.0}s)");
		yield return Math.Max(cenaA, cenaB) + 0.5;
		Marca("final: os dois transformados");
		yield return OptN("pose", 3.5);

		foreach (double s in Colidir(a, b, Opt("verboa", "Kamehameha"), Opt("verbob", "GalicGun"),
									 Opt("cora", "210,225,255"), Opt("corb", "170,40,255"), -1, "final",
									 escala: OptN("escala", 2), tiles: (int)OptN("tiles", 14))) yield return s;
		yield return OptN("depois", 6);

		SeguirDois(0, 0);
		Foco(null);
		S?.EmbateDeFoto_Limpar();
		EsconderOHeroi(false);
	}

	// =====================================================================
	// A TERRA EXPLODE, VISTA DO ESPACO
	// =====================================================================
	/// <summary>
	/// *"no momento q o beam colide a cena corta pra visao do espaco da terra explodindo e o trailer acaba
	/// ai com o titulo"*.
	///
	/// O corpo vai pra orbita (de traje: sem ele sufoca em vinte segundos) e some da tela; a camera fica
	/// no disco da Terra. A destruicao e a de producao, com o relogio levado de patamar em patamar -- a
	/// crosta acende, as rachaduras crescem -- e depois SOLTO: os ultimos segundos, o estouro e os
	/// destrocos sao o `ConsumarDestruicao` do jogo.
	/// </summary>
	private IEnumerable<double> ATerraExplode()
	{
		Tela(hud: false, chat: false);
		(string zona, float cx, float cy) = Lugar("Earth,200,236");
		foreach (double s in Viajar(zona, cx, cy)) yield return s;
		bool traje = S?.TrajeNaMochilaDeTeste(_eu) ?? false;
		EsconderOHeroi(true);
		yield return 1.0;

		string mundo = Opt("mundo", "Earth");
		if (S?.CorpoDaZona(ZoneKey.Premade(mundo)) is not { } planeta) { Nota($"`{mundo}` nao tem corpo no espaco"); yield break; }
		S.MoveToZone(_eu, S.ZonaDoEspaco, Espaco.PontoDeDecolagem(planeta));
		double t = 0;
		for (; t < 20 && !(C != null && Espaco.EhEspaco(C.Zone)); t += 0.25) yield return 0.25;
		if (C == null || !Espaco.EhEspaco(C.Zone)) { Nota("nao chegou ao espaco"); yield break; }
		yield return 3.0;
		EsconderOHeroi(true);
		Zoom((int)OptN("zoom", 2));
		Foco(V(planeta.Pos));
		Marca($"terra: {mundo} vista do ESPACO (traje {traje}, raio {planeta.Raio:0})");
		yield return OptN("antes", 5);

		_mundoDaAgonia = mundo;
		foreach (string patamar in Opt("patamares", "200,120,60,25").Split(',', StringSplitOptions.RemoveEmptyEntries))
		{
			_agonia = double.Parse(patamar, System.Globalization.CultureInfo.InvariantCulture);
			Marca($"terra: a agonia em {_agonia:0}s");
			foreach (double s in Esperar(OptN("cada", 4))) yield return s;
		}

		// o relogio SOLTO: os ultimos segundos sao do jogo
		_agonia = OptN("pavio", 7);
		SegurarAAgonia();
		_agonia = -1;
		Marca($"terra: o relogio SOLTO a {OptN("pavio", 7):0}s do fim");
		for (t = 0; t < 30 && S?.EstadoDaAgoniaNoTrailer().Fase != "Destruido"; t += 0.1) yield return 0.1;
		Marca($"terra: EXPLODIU ({t:0.0}s depois de solto)");
		yield return OptN("depois", 12);

		_mundoDaAgonia = "";
		Foco(null);
		S?.FecharAAgoniaNoTrailer();
	}

	// =====================================================================
	// AS MECANICAS: bloquear, aparar, rebater ki, o Zanzo Clash e a colisao com o QTE na tela
	// =====================================================================
	/// <summary>
	/// O BLOQUEIO E O PARRY, um depois do outro, marcados golpe a golpe. Dois figurantes: o da guarda tem
	/// dez vezes o poder do que bate (a guarda falha mais quanto mais forte e o golpe, e um golpe
	/// bloqueado ainda pode arremessar). O BLOQUEIO e a guarda erguida ha mais de 0,25 s; o PARRY e a
	/// guarda erguida NO INSTANTE do golpe (`JanelaContra`) -- a bolha azul e o contragolpe sao do jogo.
	/// </summary>
	private IEnumerable<double> AsDefesas()
	{
		Tela(hud: false, chat: false);
		(string zona, float cx, float cy) = Lugar("Earth,233,269");
		foreach (double s in Viajar(zona, cx, cy)) yield return s;
		LuzDaCena(0.5);
		Zoom((int)OptN("zoom", 4));
		EsconderOHeroi(true);
		yield return 1.0;

		Vec2 meu = S?.CorpoDoTrailer(_eu).Pos ?? default;
		Vec2 doQueBate = meu + new Vec2(-2.5f * T, 3 * T), doQueGuarda = meu + new Vec2(1.5f * T, 3 * T);
		int guarda = Figurante("Kakarotto", doQueGuarda - meu, "Goku", "Clothes_TurtleSuit", OptN("bpguarda", 3e7), Facing.West);
		int punho = Figurante("Vegeta", doQueBate - meu, "Vegeta", RoupaDoRival, OptN("bppunho", 3e6), Facing.East);
		if (guarda == 0 || punho == 0) yield break;
		SeguirDois(guarda, punho);
		yield return 2.0;

		// ---- o bloqueio
		S?.GuardarNaFotoDeCorpos(guarda, true);
		yield return 0.6;
		for (int i = 0; i < (int)OptN("bloqueios", 5); i++)
		{
			S?.RegarOKiDaVariedade(guarda);
			S?.PorNoPontoNaFotoDoBorrao(punho, doQueBate, Facing.East);
			S?.PorNoPontoNaFotoDoBorrao(guarda, doQueGuarda, Facing.West);
			yield return 0.35;
			S?.MarcarNaFotoDoBorrao(punho, guarda);
			float salto = S?.SaltarNaFotoDoBorrao(punho) ?? 0;
			Marca($"defesas: BLOQUEIO {i + 1} (o golpe saltou {salto:0} px)");
			yield return 0.9;
		}
		S?.GuardarNaFotoDeCorpos(guarda, false);
		yield return 1.8;

		// ---- o parry: a guarda sobe no MESMO passo do golpe
		for (int i = 0; i < (int)OptN("parrys", 4); i++)
		{
			S?.RegarOKiDaVariedade(guarda);
			S?.PorNoPontoNaFotoDoBorrao(punho, doQueBate, Facing.East);
			S?.PorNoPontoNaFotoDoBorrao(guarda, doQueGuarda, Facing.West);
			yield return 0.5;
			S?.MarcarNaFotoDoBorrao(punho, guarda);
			S?.GuardarNaFotoDeCorpos(guarda, true);
			S?.SaltarNaFotoDoBorrao(punho);
			Marca($"defesas: PARRY {i + 1}");
			yield return 1.2;
			S?.GuardarNaFotoDeCorpos(guarda, false);
			yield return 1.7;      // a recarga do contragolpe (`RecargaDoContra`, 1,5 s)
		}

		SeguirDois(0, 0);
		Foco(null);
		S?.LimparAFoto();
		EsconderOHeroi(false);
	}

	/// <summary>
	/// O KI REBATIDO: a bola que volta em quem atirou, e o raio que desvia. E o parry de ki do jogo
	/// (`TentarParryDeKi`): a guarda erguida ate 0,25 s antes do contato, por quem e mais forte que o tiro.
	/// A cena espera a bola (ou a cabeca do raio) chegar perto e so entao ergue a guarda.
	/// </summary>
	private IEnumerable<double> ORebateDeKi()
	{
		Tela(hud: false, chat: false);
		(string zona, float cx, float cy) = Lugar("Earth,233,269");
		foreach (double s in Viajar(zona, cx, cy)) yield return s;
		LuzDaCena(OptN("hora", 0.78));
		Zoom((int)OptN("zoom", 3));
		EsconderOHeroi(true);
		yield return 1.0;

		Vec2 meu = S?.CorpoDoTrailer(_eu).Pos ?? default;
		Vec2 deQuemAtira = meu + new Vec2(-4.5f * T, 3 * T), deQuemRebate = meu + new Vec2(4.5f * T, 3 * T);
		int atira = Figurante("Vegeta", deQuemAtira - meu, "Vegeta", RoupaDoRival, OptN("bpatira", 1e6), Facing.East);
		int rebate = Figurante("Kakarotto", deQuemRebate - meu, "Goku", "Clothes_TurtleSuit", OptN("bprebate", 3e7), Facing.West);
		if (atira == 0 || rebate == 0) yield break;
		S?.ArmarParaAVariedade(atira);
		SeguirDois(atira, rebate);
		yield return 2.0;

		foreach (string verbo in Opt("tiros", "Basic_Blast,Basic_Blast,Ki_Wave,Kamehameha").Split(','))
		{
			S?.LimparOsTirosDaVariedade(atira);
			S?.GuardarNaFotoDeCorpos(rebate, false);
			S?.RegarOKiDaVariedade(atira);
			S?.RegarOKiDaVariedade(rebate);
			S?.PorNoPontoNaFotoDoBorrao(atira, deQuemAtira, Facing.East);
			S?.PorNoPontoNaFotoDoBorrao(rebate, deQuemRebate, Facing.West);
			yield return 1.6;      // a recarga do contragolpe, e a mesa limpa

			string resposta = S?.TecnicaNoTrailer(atira, verbo) ?? "";
			Marca($"rebate {verbo}: DISPAROU" + (resposta.Length > 0 ? $" -- \"{resposta}\"" : ""));

			// A GUARDA SOBE UM TEMPO ANTES DO CONTATO, no meio da janela do parry (`MeleeResolver.JanelaContra`). Era
			// uma DISTANCIA fixa -- um corpo pra bola, dois pra cabeca do raio --, calibrada no passo antigo dos tiros:
			// a 16 tiles por segundo (dono, 2026-10-08) a bola anda meio tile por tique e cruzava o "um corpo" ja
			// encostando, com a guarda subindo depois do impacto. A distancia agora sai da velocidade do proprio tiro.
			bool chegou = false;
			float contato = verbo == "Basic_Blast" ? 36 : 56;
			for (double t = 0; t < 8 && !chegou; t += 0.016)
			{
				var tiro = S?.TiroDaVariedade(atira) ?? default;
				float perto = contato + (S?.VelocidadeDoTiroNoTrailer(atira) ?? 0) * (float)OptN("antes", 0.12);
				chegou = tiro.Achou && (tiro.Pos - deQuemRebate).Length <= perto;
				if (!chegou) yield return 0.016;
			}
			S?.GuardarNaFotoDeCorpos(rebate, true);
			Marca($"rebate {verbo}: a GUARDA sobe ({(chegou ? "o tiro chegou" : "o tiro NAO chegou")})");
			yield return verbo == "Basic_Blast" ? 3.0 : OptN("desvio", 4.5);
		}

		SeguirDois(0, 0);
		Foco(null);
		S?.GuardarNaFotoDeCorpos(rebate, false);
		S?.LimparOsTirosDaVariedade(atira);
		S?.LimparAFoto();
		EsconderOHeroi(false);
	}

	private char _letraDoClash;
	private double _letraDesde;

	private void AnotarALetra(char c, int ms)
	{
		_letraDoClash = c;
		_letraDesde = _relogio;
	}

	/// <summary>
	/// O ZANZO CLASH COM O QUICK TIME EVENT NA TELA: o heroi e um dos dois (o painel de letras so
	/// aparece pra quem esta no embate), e responde como o jogador responde -- a letra pedida, pelo
	/// pacote de sempre (`SendClashTecla`), depois de olhar pra ela por um instante. `rodadas=` quantos
	/// embates filmar.
	/// </summary>
	private IEnumerable<double> OZanzoClash()
	{
		Tela(hud: false, chat: false);
		(string zona, float cx, float cy) = Lugar("Earth,233,269");
		foreach (double s in Viajar(zona, cx, cy)) yield return s;
		LuzDaCena(OptN("hora", 0.5));
		Zoom((int)OptN("zoom", 3));
		VestirOHeroi();
		S?.ArmarParaAVariedade(_eu);
		yield return 1.0;

		int rival = Figurante("Vegeta", new Vec2(3 * T, 0), "Vegeta", RoupaDoRival, OptN("bprival", 3e6), Facing.West);
		if (rival == 0 || C == null) yield break;
		S?.ArmarParaAVariedade(rival);
		foreach (double s in Encarar(Vector2.Right, P(Aqui))) yield return s;
		yield return 2.0;

		C.ClashTeclaPedida += AnotarALetra;
		for (int n = 0; n < (int)OptN("rodadas", 2); n++)
		{
			S?.RegarOKiDaVariedade(_eu);
			S?.RegarOKiDaVariedade(rival);
			S?.CurarDeTeste(_eu);
			_letraDoClash = '\0';
			bool foi = S?.ZanzoClashNoTrailer(_eu, rival) ?? false;
			Marca($"zanzo: rodada {n + 1} {(foi ? "COMECOU" : "RECUSADA")}");
			double reacao = OptN("reacao", 0.22);
			for (double t = 0; t < 12 && (S?.EmZanzoNoTrailer(_eu) ?? false); t += 0.03)
			{
				if (_letraDoClash != '\0' && _relogio - _letraDesde >= reacao) { C.SendClashTecla(_letraDoClash); _letraDoClash = '\0'; }
				yield return 0.03;
			}
			Marca($"zanzo: rodada {n + 1} ACABOU");
			yield return OptN("entre", 4.5);
		}
		C.ClashTeclaPedida -= AnotarALetra;

		S?.LimparAFoto();
	}

	/// <summary>
	/// A COLISAO DE KI COM O QUICK TIME EVENT NA TELA: o heroi e o lado A (o painel de letras so aparece
	/// pra quem disputa). Ele comeca respondendo mal, e vira -- como na cena do Kaio-ken, so que visto
	/// de dentro.
	/// </summary>
	private IEnumerable<double> AColisaoComOQte()
	{
		Tela(hud: false, chat: false);
		(string zona, float cx, float cy) = Lugar("Earth,233,269");
		foreach (double s in Viajar(zona, cx, cy)) yield return s;
		LuzDaCena(OptN("hora", 0.80));
		Zoom((int)OptN("zoom", 3));
		VestirOHeroi();
		double bp = OptN("bp", 3e6);
		S?.PoderNoTrailer(_eu, bp);
		yield return 1.0;

		int rival = Figurante("Vegeta", new Vec2(8 * T, 3 * T), "Vegeta", RoupaDoRival, OptN("bprival", bp), Facing.West);
		if (rival == 0) yield break;
		yield return 1.5;

		// O RIVAL E UM POUCO MAIS FRACO (`razao` < 1): o que esta tomada mostra e o PAINEL -- a letra pedida,
		// o acerto, a barra andando --, e pra isso a disputa tem que durar e o heroi tem que ir empurrando.
		foreach (double s in Colidir(_eu, rival, "Kamehameha", "GalicGun", Opt("cora", "40,120,255"), Opt("corb", "190,60,255"),
									 OptN("vira", -1), "embatehud", razao: OptN("razao", 0.8))) yield return s;
		yield return OptN("depois", 5);

		SeguirDois(0, 0);
		Foco(null);
		S?.EmbateDeFoto_Limpar();
	}

	// =====================================================================
	// OS SISTEMAS: a base, as pessoas, a danca da fusao
	// =====================================================================
	/// <summary>
	/// A BASE: o heroi abre a aba de tecnologia, fabrica as maquinas e as instala uma a uma em volta
	/// dele -- pelos tres comandos do jogador (`construir`, `posicionar`, `aparafusar`). A tomada precisa
	/// da flag `--techteste` (nivel de tecnologia e zeni pra pagar a fila inteira). `maquinas=` troca a
	/// lista: `Id,dx,dy|...`, em tiles a partir de onde o heroi comecou.
	/// </summary>
	private IEnumerable<double> ABase()
	{
		Tela(hud: true, chat: false);
		(string zona, float cx, float cy) = Lugar("Earth,233,269");
		foreach (double s in Viajar(zona, cx, cy)) yield return s;
		LuzDaCena();
		Zoom((int)OptN("zoom", 3));
		VestirOHeroi();
		yield return 1.5;

		if (MenuJogo.Instancia is { } menu)
		{
			menu.Abrir();
			menu.IrPara("Tech");
			Marca("base: a aba de TECNOLOGIA");
			yield return OptN("menu", 5);
			menu.Fechar();
			yield return 0.6;
		}
		Tela(hud: false, chat: false);

		string fila = Opt("maquinas", "Research_Station,-4,-3|Gravity,0,-4|Regenerator,4,-3|Spacepod,-5,2|Rocket_Ship,5,2|Bio_Field,0,3");
		foreach (string item in fila.Split('|', StringSplitOptions.RemoveEmptyEntries))
		{
			string[] p = item.Split(',');
			if (p.Length < 3) continue;
			float dx = float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture);
			float dy = float.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture);

			// o heroi vai ate um tile abaixo do lugar (instalar pede a maquina a tres tiles do corpo)
			Por(cx + dx, cy + dy + 1.6f, Facing.North);
			yield return 0.5;
			C?.SendTech("construir", p[0]);
			yield return 0.5;
			Vector2 onde = CentroV(cx + dx, cy + dy);
			C?.SendTech("posicionar", $"{p[0]}/{onde.X.ToString("0", System.Globalization.CultureInfo.InvariantCulture)}/{onde.Y.ToString("0", System.Globalization.CultureInfo.InvariantCulture)}");
			yield return 0.6;
			C?.SendTech("aparafusar");
			Marca($"base: {p[0]} instalada em ({cx + dx:0},{cy + dy:0})");
			yield return OptN("cada", 1.6);
		}

		// a base pronta, vista de longe
		Por(cx, cy, Facing.South);
		yield return 0.4;
		Zoom((int)OptN("zoomfinal", 2));
		Marca("base: PRONTA");
		yield return OptN("depois", 6);
	}

	/// <summary>
	/// A ROUPA NA MOCHILA: o heroi abre a mochila, TIRA uma peca do corpo (ela cai na grade), veste OUTRA
	/// que ja estava guardada e depois poe a primeira de volta -- pelos mesmos dois verbos que os botoes da
	/// tela mandam (`item_despir`, `item_vestir`). `cada=` segundos em cada estado; `outra=` a peca guardada.
	/// </summary>
	private IEnumerable<double> ARoupaNaMochila()
	{
		Tela(hud: true, chat: true);
		(string zona, float cx, float cy) = Lugar("Earth,233,269");
		foreach (double s in Viajar(zona, cx, cy)) yield return s;
		LuzDaCena();
		Zoom((int)OptN("zoom", 3));
		VestirOHeroi();
		yield return 1.5;

		if (C is not { } cli || GetTree().Root.FindChild("Inventario", true, false) is not TelaDeInventario mochila)
		{ Nota("sem cliente ou sem a tela da mochila"); yield break; }
		string Estado() => $"vestindo {cli.Vestindo.Count}: "
			+ string.Join(" + ", cli.Vestindo.Select(p => Jandirus.Core.Items.RoupaGuardada.De(p).Nome))
			+ $" | mochila {cli.Mochila.Ocupados}: " + string.Join(", ", cli.Mochila.Pilhas.Select(p => p.Id));

		// a peca que ja esta guardada, pra haver com o que TROCAR
		string outra = S?.GuardarRoupaNoTrailer(_eu, Opt("outra", "Clothes_Cape"), Opt("coroutra", "170,30,30")) ?? "";
		yield return 0.6;

		mochila.Abrir();
		Marca($"roupa: a MOCHILA aberta -- {Estado()}");
		yield return OptN("cada", 2.5);
		if (cli.Vestindo.Count == 0) { Nota("o heroi nao veste nada: nada a tirar"); yield break; }

		string tirada = Jandirus.Core.Items.RoupaGuardada.De(cli.Vestindo[0]).Id;
		cli.SendVerbo($"item_{Jandirus.Core.Items.RoupaGuardada.AcaoTirar}", tirada);
		yield return 0.8;
		Marca($"roupa: TIROU a primeira peca -- {Estado()}");
		yield return OptN("cada", 2.5);

		if (outra.Length > 0)
		{
			cli.SendVerbo($"item_{Jandirus.Core.Items.RoupaGuardada.AcaoVestir}", outra);
			yield return 0.8;
			Marca($"roupa: VESTIU a outra -- {Estado()}");
			yield return OptN("cada", 2.5);
		}

		cli.SendVerbo($"item_{Jandirus.Core.Items.RoupaGuardada.AcaoVestir}", tirada);
		yield return 0.8;
		Marca($"roupa: VESTIU a primeira de volta -- {Estado()}");
		yield return OptN("cada", 2.5);

		mochila.Fechar();
		yield return 1.0;
	}

	/// <summary>
	/// AS PESSOAS: a aba People com amigos, rivais e inimigos. O convivio de verdade leva dezenas de
	/// minutos ao lado de outro JOGADOR (NPC nao entra na lista de ninguem), entao os conhecidos desta
	/// tomada sao PLANTADOS pelo mesmo evento que o fio dispara (`ConhecidosDeTeste`), com o retrato de
	/// cada um na memoria de "como eu vi da ultima vez" -- a aba que os desenha e a do jogo.
	/// </summary>
	private IEnumerable<double> AGente()
	{
		Tela(hud: true, chat: false);
		(string zona, float cx, float cy) = Lugar("Earth,247,432");
		foreach (double s in Viajar(zona, cx, cy)) yield return s;
		LuzDaCena();
		Zoom((int)OptN("zoom", 3));
		VestirOHeroi();
		yield return 1.5;
		if (C is not { } cli || MenuJogo.Instancia is not { } menu) { Nota("sem cliente ou sem menu"); yield break; }

		VisualCatalog cat = VisualCatalog.Parse(Godot.FileAccess.GetFileAsString("res://Assets/Data/visual.json"));
		Appearance Retrato(string cabelo, string roupa)
		{
			Appearance ap = cli.Visual.Copiar();
			ap.Cabelo = cabelo;
			ap.CorCabelo = null;
			ap.Roupa = [.. Pecas(roupa).Select(n => cat.Peca(n)).Where(c => c != null).Select(c => new PecaDeRoupa(c!, null))];
			return ap;
		}

		VistosDeGente.AnotarDeTeste("Kuririn", "Human", "Male", Retrato("Bald", "Clothes_TurtleSuit"));
		VistosDeGente.AnotarDeTeste("Vegeta", "Saiyan", "Male", Retrato("Vegeta", RoupaDoRival));
		VistosDeGente.AnotarDeTeste("Gohan", "Halfbreed", "Male", Retrato("Kid Gohan", "Clothes_GohanGi"));
		VistosDeGente.AnotarDeTeste("Raditz", "Saiyan", "Male", Retrato("Raditz", "RaditzArmorTobiUchiha"));
		VistosDeGente.AnotarDeTeste("Yamcha", "Human", "Male", Retrato("Yamcha", "Clothes_GiTop+Clothes_GiBottom"));
		cli.ConhecidosDeTeste([
			new GameClient.ConhecidoInfo("trailer-kuririn", "Kuririn", "Human", "Normal", 240, (byte)Relacao.MuitoBom, 96f, 0f, false),
			new GameClient.ConhecidoInfo("trailer-gohan", "Gohan", "Halfbreed", "Normal", 300, (byte)Relacao.Amor, 100f, 0f, false),
			new GameClient.ConhecidoInfo("trailer-vegeta", "Vegeta", "Saiyan", "Elite", 150, (byte)Relacao.RivalBom, 48f, 30f, true),
			new GameClient.ConhecidoInfo("trailer-yamcha", "Yamcha", "Human", "Normal", 60, (byte)Relacao.Bom, 35f, 0f, false),
			new GameClient.ConhecidoInfo("trailer-raditz", "Raditz", "Saiyan", "Low-Class", 40, (byte)Relacao.Odio, 0f, 92f, false),
		]);

		menu.Abrir();
		menu.IrPara("People");
		menu.ForcarRedesenho();
		Marca("gente: a aba PEOPLE");
		yield return OptN("cada", 5);

		// a lista e mais alta que a tela: rola ate o fim, devagar
		if (menu.PaginaDeTeste("People") is { } pagina && Rolagem(pagina) is { } rolagem)
		{
			double seg = OptN("rola", 6);
			int fim = (int)rolagem.GetVScrollBar().MaxValue;
			for (double t = 0; t < seg; t += 0.05)
			{
				rolagem.ScrollVertical = (int)(fim * Math.Min(1, t / seg));
				yield return 0.05;
			}
			Marca("gente: o fim da lista");
			yield return 2.0;
		}

		menu.Fechar();
		VistosDeGente.EsquecerDeTeste();
		yield return 0.5;
	}

	/// <summary>A caixa de rolagem que contem este node (a pagina da aba mora dentro de uma).</summary>
	private static ScrollContainer? Rolagem(Node n)
	{
		for (Node? p = n; p != null; p = p.GetParent())
			if (p is ScrollContainer s) return s;
		return null;
	}

	/// <summary>
	/// A DANCA DA FUSAO COM O QUICK TIME EVENT NA TELA. O convite e o de producao (`Convidar`, tipo
	/// Danca), o parceiro aceita, e ai comeca o QTE dos DOIS -- o heroi responde letra por letra, pelo
	/// pacote de sempre; o parceiro, que nao tem teclado, responde pela agenda da maquina. Acertando
	/// tudo, a cinematica da fusao e o corpo novo sao do jogo.
	/// </summary>
	private IEnumerable<double> ADancaDaFusao()
	{
		Tela(hud: false, chat: false);
		(string zona, float cx, float cy) = Lugar("Earth,233,269");
		foreach (double s in Viajar(zona, cx, cy)) yield return s;
		LuzDaCena(0.68);
		Zoom((int)OptN("zoom", 4));
		VestirOHeroi();
		if (C == null) yield break;

		int outro = S?.ForjarParaAFotoDeFusao(_eu, new Vec2(T, 0), "Vegeta", OptN("bp", 3e6), "Vegeta") ?? 0;
		if (outro == 0) { Nota("o parceiro de fusao nao nasceu"); yield break; }
		S?.VestirNoTrailer(outro, "Vegeta", Pecas(RoupaDoRival));
		foreach (double s in Encarar(Vector2.Right, P(Aqui))) yield return s;
		S?.ApontarNaFotoDeCorpos(outro, Facing.West);
		yield return 2.5;

		// A SKILL DA DANCA E DADA NO MESMO PASSO DO CONVITE: ela e de CARGO (`/datum/skill/rank/`), e o livro
		// de quem nao tem o cargo e reconciliado em seguida -- na primeira tomada, dois segundos e meio
		// depois de dada ela ja nao estava la, e o convite voltou "voce nao sabe a Danca da Fusao".
		C.ClashTeclaPedida += AnotarALetra;
		_letraDoClash = '\0';
		S?.PorPertoNaFotoDeFusao(outro, _eu, new Vec2(T, 0));
		S?.PrepararAMetamoroNaFotoDeFusao(_eu, outro);
		S?.ZerarRecargaNaFotoDeFusao(_eu, outro);
		S?.PassoDaDancaNoTrailer(outro);      // (ainda nao ha danca: so entrega o teclado ao parceiro)
		(bool entrou, RecusaDeFusao porque) = S?.ConvidarNaFotoDeFusao(_eu, outro, TipoDeFusao.Danca) ?? (false, default);
		bool aceitou = S?.AceitarNaFotoDeFusao(outro) ?? false;
		Marca($"danca: convite {(entrou ? "entrou" : $"RECUSADO ({porque})")}, {(aceitou ? "aceito -- a DANCA comeca" : "nao aceito")}");

		double reacao = OptN("reacao", 0.25), t = 0;
		bool emCena = false;
		for (; t < 40 && !(S?.EstadoDaCoreografiaNaFotoDeFusao(_eu, outro).Fundido ?? false); t += 0.03)
		{
			if (_letraDoClash != '\0' && _relogio - _letraDesde >= reacao)
			{
				C.SendClashTecla(_letraDoClash);
				_letraDoClash = '\0';
				S?.PassoDaDancaNoTrailer(outro);      // o parceiro acerta junto
			}
			if (!emCena && S?.EstadoDaCoreografiaNaFotoDeFusao(_eu, outro).EmCena == true) { emCena = true; Marca("danca: a CINEMATICA comeca"); }
			yield return 0.03;
		}
		C.ClashTeclaPedida -= AnotarALetra;
		Marca($"danca: FUNDIDOS aos {t:0.0}s");
		yield return OptN("depois", 6);

		S?.SepararNaFotoDeFusao(_eu, "fim da cena");
		yield return 1.5;
		S?.LimparAFotoDeFusao();
	}

	/// <summary>
	/// O BIO-ANDROIDE, DA LARVA A FORMA PERFEITA. E a linha de corpos da raca (`BioAndroids.Corpos`: a
	/// larva, o Imperfeito, o Semi-Perfeito e o Perfeito), mostrada num corpo so, degrau por degrau.
	///
	/// O QUE ESTA CENA NAO E: a criacao inteira. No jogo ela leva horas e pede outras pessoas (o
	/// laboratorio, o DNA colhido de jogadores caidos, a gestacao, as absorcoes), e o palco que a
	/// encena por flag (`--biovivo`) acerta o relogio do mundo a cada tique -- a tomada saiu com o dia
	/// piscando. Aqui o corpo e um bio-androide de molde e a cena troca o corpo dele pelo indice, que e
	/// o mesmo campo que a evolucao de verdade escreve (`SubirDegrauDoBio`).
	/// </summary>
	private IEnumerable<double> OBioAndroide()
	{
		Tela(hud: false, chat: false);
		(string zona, float cx, float cy) = Lugar("Earth,233,269");
		foreach (double s in Viajar(zona, cx, cy)) yield return s;
		LuzDaCena();
		Zoom((int)OptN("zoom", 5));
		EsconderOHeroi(true);
		yield return 1.0;

		int bio = Ator("cell", new Vec2(0, 3 * T), "", "", OptN("bp", 2e8));
		if (bio == 0) yield break;
		S?.DeixaNoTrailer(bio, esperar: true);
		S?.ApontarNaFotoDeCorpos(bio, Facing.South);
		Seguir(bio);

		string[] nomes = ["a LARVA", "o IMPERFEITO", "o SEMI-PERFEITO", "o PERFEITO"];
		for (int corpo = 0; corpo < nomes.Length; corpo++)
		{
			S?.CorpoDoAtorNoTrailer(bio, corpo);
			S?.ApontarNaFotoDeCorpos(bio, Facing.South);
			Marca($"bio: {nomes[corpo]}");
			yield return OptN("cada", 3.0);
		}

		// o Perfeito mostra o que tem: carrega, e solta um raio
		S?.DePeNoTrailer(bio);
		S?.ApontarNaFotoDeCorpos(bio, Facing.East);
		Zoom((int)OptN("zoomfinal", 3));
		yield return 0.6;
		S?.RaioNoTrailer(bio, Opt("verbo", "Kamehameha"), Opt("cor", "90,230,170"));
		Marca("bio: o Perfeito DISPARA");
		yield return OptN("depois", 5);

		S?.SoltarOsRaiosNoTrailer(bio, bio);
		Foco(null);
		S?.LimparOTrailer();
		EsconderOHeroi(false);
	}
}
