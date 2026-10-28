using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--cabecateste` (2026-10-27) -- o pedido 119 do dono: *"se vc for nocauteado por dano na cabeça, vc desmaia [...] e vc demora o
/// dobro do tempo pra levantar naturalmente, torso mantem igual o atual, se vc tentar falar caido com torso ferido o jogo vai fazer vc
/// gagejar"*, e o adendo: *"Se vc desmaiar pelo tronco mas o cara ir la e socar sua cabeça vc tb pode desmaiar igual"*. Roda no BOOT, com
/// corpos de PESSOA forjados, e mede pelas portas de producao -- o `Nocautear`, o `Tick` do combate e a fala digitada (`Falar`, lida pelo
/// que cada ouvinte RECEBEU, `EscutaDeEntregas`):
///   C1  a cabeca abaixo da linha na queda marca o desmaio PELA CABECA; o tronco abaixo da linha (cabeca inteira) nao;
///   C2  o adendo: quem caiu pelo tronco e leva o golpe na cabeca (ela cai abaixo da linha no chao) passa a desmaiado pela cabeca;
///   C3  o DOBRO no relogio: o nocaute cronometrado de 10 s levanta em 10 s pelo tronco e em 20 s pela cabeca;
///   C4  o DOBRO na cura: o nucleo que sara aos 12 s levanta o tronco aos 12 s e a cabeca aos 24 s;
///   C5  quem LEVANTA O CORPO POR FORA (uma cura, um revive) levanta na hora, mesmo desmaiado pela cabeca -- e levantar apaga a marca;
///   C6  a GAGUEIRA no say: caido com o tronco ferido, "socorro" chega tropecado ("s-s..."); de pe, ou com o tronco inteiro, ou no OOC,
///       chega igual;
///   C7  a gagueira em si: o exemplo do dono sai com os tres tropecos, a mesma fala sai igual duas vezes, e a pontuacao passa.
/// E o pedido 127 (2026-10-27): *"dano na cabeça deveria tb fazer o chat gagejar"*, e a ideia do jogador gingo pro cerebro a 40%:
///   C8  a CABECA ferida gagueja caida (de pe nao); com cabeca e tronco feridos vale a PIOR das duas;
///   C9  a fala longa gaguejada cabe no teto do fio (`MaxFala`) -- acima dele o cliente a recebia VAZIA;
///   C10 o CEREBRO a 40% troca as palavras da fala de boca (say e sussurro), de pe e caido (caido, gagueja por cima); a 41% e no OOC, igual;
///   C11 a fala sem sentido em si: a ligacao e o nome ficam, a troca tem o jeito do fim e a caixa, sai igual duas vezes, portugues por portugues.
///
///     Godot --headless --path . --server --port 11751 --cabecateste
/// </summary>
public sealed partial class GameServer
{
	private int _cbOk, _cbFalhou;
	private const int IdBaseDaCabecaDeTeste = 93_600;

	private void AfirmarCb(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _cbOk++; GD.Print($"[cabeca]   OK    {oque}   [{detalhe}]"); return; }
		_cbFalhou++;
		GD.PrintErr($"[cabeca]   FALHA {oque}   {detalhe}");
	}

	public void RodarBancadaDaCabeca()
	{
		_cbOk = _cbFalhou = 0;
		GD.Print("[cabeca] ================ O DESMAIO PELA CABECA E A GAGUEIRA (dono, 2026-10-27) ================");

		ZoneKey zona = ZoneKey.Premade("Earth");
		var corpos = new List<ServerPlayer>();
		ServerPlayer Forjar(int i, string nome, float tilesX)
		{
			var p = new ServerPlayer
			{
				Id = IdBaseDaCabecaDeTeste + i, Peer = null, Name = nome, Race = "Human", Genero = "Male", Idade = 25, Zone = zona,
				Pos = new Vec2(tilesX * ZoneCollision.TileSize, 0), Conta = $"bancada_cabeca_{i}", Slot = 0,
				Ficha = new Jandirus.Core.Stats.Fighter { Race = "Human", BP = 10_000 }, Livro = new Jandirus.Core.Skills.SkillBook(),
			};
			p.Ficha.Class = "Normal";
			PorNoMundo(p);
			corpos.Add(p);
			return p;
		}

		try
		{
			ServerPlayer cab = Forjar(1, "bancada: cai pela cabeca", 0);
			ServerPlayer tro = Forjar(2, "bancada: cai pelo tronco", 1);
			ServerPlayer ouvinte = Forjar(3, "bancada: quem ouve", 2);
			Forjar(4, "Nova", 3);   // (o pedido 127) o nome do exemplo do gingo, no mundo: a fala sem sentido o deixa no lugar

			// ---- C1) a queda
			Ferir(cab, "Cabeca", 0.15);
			cab.Combate.Nocautear(60, porVital: true);
			Ferir(tro, "Torso", 0.15);
			tro.Combate.Nocautear(60, porVital: true);
			AfirmarCb("C1. a cabeca abaixo da linha na queda e desmaio PELA CABECA; o tronco abaixo da linha (cabeca inteira) nao",
					  cab.Ficha.KO && cab.Combate.DesmaioPelaCabeca && tro.Ficha.KO && !tro.Combate.DesmaioPelaCabeca,
					  $"cabeca: KO {cab.Ficha.KO}, marca {cab.Combate.DesmaioPelaCabeca} | tronco: KO {tro.Ficha.KO}, marca {tro.Combate.DesmaioPelaCabeca}");

			// ---- C2) o adendo: o golpe na cabeca de quem caiu pelo tronco
			tro.Combate.Tick(0.1);
			bool antesDoGolpe = tro.Combate.DesmaioPelaCabeca;
			Ferir(tro, "Cabeca", 0.10);
			tro.Combate.Tick(0.1);
			AfirmarCb("C2. (adendo) quem caiu pelo TRONCO e leva o golpe na cabeca, ja no chao, passa a desmaiado PELA CABECA",
					  !antesDoGolpe && tro.Ficha.KO && tro.Combate.DesmaioPelaCabeca,
					  $"antes do golpe {antesDoGolpe}, depois {tro.Combate.DesmaioPelaCabeca}");

			// ---- C3) o dobro no relogio
			Inteiro(cab); Inteiro(tro);
			Ferir(cab, "Cabeca", 0.15);
			Ferir(tro, "Torso", 0.15);
			cab.Combate.Nocautear(10);
			tro.Combate.Nocautear(10);
			double emPeCab = Cronometrar(cab, 60), emPeTro = Cronometrar(tro, 60);
			AfirmarCb("C3. o DOBRO no relogio: o nocaute de 10 s levanta em 10 s pelo tronco e em 20 s pela cabeca",
					  Math.Abs(emPeTro - 10) < 0.25 && Math.Abs(emPeCab - 20) < 0.25,
					  $"tronco {emPeTro:0.0} s, cabeca {emPeCab:0.0} s");

			// ---- C4) o dobro na cura: o nucleo sara aos 12 s (depois do piso de 8 s)
			Inteiro(cab); Inteiro(tro);
			Ferir(cab, "Cabeca", 0.15);
			Ferir(tro, "Torso", 0.15);
			cab.Combate.Nocautear(MeleeResolver.TetoDoNocaute, porVital: true);
			tro.Combate.Nocautear(MeleeResolver.TetoDoNocaute, porVital: true);
			double curaCab = Cronometrar(cab, 120, saraAos: 12, membro: "Cabeca"), curaTro = Cronometrar(tro, 120, saraAos: 12, membro: "Torso");
			AfirmarCb("C4. o DOBRO na cura: o nucleo que sara aos 12 s levanta o tronco aos 12 s, e a cabeca aos 24 s",
					  Math.Abs(curaTro - 12) < 0.25 && Math.Abs(curaCab - 24) < 0.25,
					  $"tronco {curaTro:0.0} s, cabeca {curaCab:0.0} s");

			// ---- C5) levantar por fora e na hora
			Inteiro(cab);
			Ferir(cab, "Cabeca", 0.15);
			cab.Combate.Nocautear(60, porVital: true);
			bool marcou = cab.Combate.DesmaioPelaCabeca;
			cab.Combate.Levantar();
			AfirmarCb("C5. quem levanta o corpo POR FORA (uma cura, um revive) levanta na hora, desmaiado pela cabeca ou nao -- e a marca se apaga",
					  marcou && !cab.Ficha.KO && !cab.Combate.DesmaioPelaCabeca && cab.Combate.TempoDesmaiado == 0,
					  $"marcou {marcou}, KO {cab.Ficha.KO}, marca depois {cab.Combate.DesmaioPelaCabeca}");

			// ---- C6) a gagueira no say
			Inteiro(tro);
			Ferir(tro, "Torso", 0.40);
			tro.Combate.Nocautear(60, porVital: true);
			string caidoFerido = OuvidoPor(ouvinte, tro, Protocol.Fala.Diz, "socorro");
			string caidoNoOoc = OuvidoPor(ouvinte, tro, Protocol.Fala.Ooc, "socorro");
			tro.Combate.Levantar();
			string dePe = OuvidoPor(ouvinte, tro, Protocol.Fala.Diz, "socorro");
			Inteiro(tro);
			tro.Combate.Nocautear(60);
			string caidoInteiro = OuvidoPor(ouvinte, tro, Protocol.Fala.Diz, "socorro");
			tro.Combate.Levantar();
			AfirmarCb("C6. a GAGUEIRA: caido com o tronco ferido, \"socorro\" chega tropecado (\"s-s...\"); de pe, com o tronco inteiro ou no OOC, igual",
					  caidoFerido.StartsWith("s-s") && caidoFerido != "socorro" && dePe == "socorro" && caidoInteiro == "socorro"
					  && caidoNoOoc == "socorro",
					  $"caido e ferido \"{caidoFerido}\" | OOC \"{caidoNoOoc}\" | de pe \"{dePe}\" | caido inteiro \"{caidoInteiro}\"");

			// ---- C7) a gagueira em si
			string forte = Gagueira.Gaguejar("socorro", 0.20), deNovo = Gagueira.Gaguejar("socorro", 0.20);
			string pontuada = Gagueira.Gaguejar("Socorro, alguem!!!", 0.30), maiuscula = Gagueira.Gaguejar("Socorro", 0.20);
			string semGagueira = Gagueira.Gaguejar("socorro", 0.90);
			bool soLetrasDaPalavra = forte.Replace("-", "").Distinct().All(c => "socr".Contains(c));
			AfirmarCb("C7. a gagueira: o exemplo do dono comeca em \"s-s\", sai igual duas vezes, so com as letras dele; a pontuacao passa; tronco a 90% nao gagueja",
					  forte.StartsWith("s-ss") && forte == deNovo && soLetrasDaPalavra && maiuscula.StartsWith("S-ss")
					  && pontuada != "Socorro, alguem!!!" && pontuada.EndsWith("!!!") && pontuada.Contains(", ") && semGagueira == "socorro",
					  $"\"{forte}\" | \"{maiuscula}\" | \"{pontuada}\" | 90%: \"{semGagueira}\"");

			// ---- C8) (o pedido 127) a CABECA ferida tambem gagueja -- caida, como o tronco; e com as duas feridas vale a pior
			Inteiro(tro);
			Ferir(tro, "Cabeca", 0.40);
			tro.Combate.Nocautear(60);
			string cabecaCaida = OuvidoPor(ouvinte, tro, Protocol.Fala.Diz, "socorro");
			tro.Combate.Levantar();
			string cabecaDePe = OuvidoPor(ouvinte, tro, Protocol.Fala.Diz, "socorro");
			const string Frase = "socorro, alguem me ajude aqui agora";
			Ferir(tro, "Cabeca", 0.25);
			Ferir(tro, "Torso", 0.70);
			tro.Combate.Nocautear(60);
			string asDuas = OuvidoPor(ouvinte, tro, Protocol.Fala.Diz, Frase);
			tro.Combate.Levantar();
			AfirmarCb("C8. (o pedido 127) a CABECA ferida tambem gagueja, so caida: a 40% com o tronco inteiro sai \"s-s...\", de pe sai igual; com as duas feridas vale a PIOR (a cabeca a 25%, e nao o tronco a 70%)",
					  cabecaCaida.StartsWith("s-s") && cabecaDePe == "socorro"
					  && asDuas == Gagueira.Gaguejar(Frase, 0.25) && asDuas != Gagueira.Gaguejar(Frase, 0.70),
					  $"caida \"{cabecaCaida}\" | de pe \"{cabecaDePe}\" | as duas \"{asDuas}\"");

			// ---- C9) a fala tropecada que cresce cabe no teto do fio: acima de `MaxFala` o cliente a recebia VAZIA (`GetString`)
			Inteiro(tro);
			Ferir(tro, "Torso", 0.21);
			tro.Combate.Nocautear(60, porVital: true);
			string longa = string.Concat(Enumerable.Repeat("socorro ", Protocol.MaxFala / 8)).Trim();
			string longaOuvida = OuvidoPor(ouvinte, tro, Protocol.Fala.Diz, longa);
			tro.Combate.Levantar();
			AfirmarCb("C9. a fala longa gaguejada CABE no teto do fio (o cliente le no maximo `MaxFala` letras, e acima disso ela chegava VAZIA)",
					  longaOuvida.Length > 0 && longaOuvida.Length <= Protocol.MaxFala && longaOuvida.StartsWith("s-s"),
					  $"{longa.Length} letras digitadas, {longaOuvida.Length} entregues (teto {Protocol.MaxFala})");

			// ---- C10) (o pedido 127) o CEREBRO em 40% ou menos: a fala de boca vira nonsense, de pe e caido; o OOC passa igual
			Inteiro(tro);
			const string DoGingo = "Hey, Nova, How's the training Going?";
			Ferir(tro, "Cerebro", 0.41);
			string cerebro41 = OuvidoPor(ouvinte, tro, Protocol.Fala.Diz, DoGingo);
			Ferir(tro, "Cerebro", 0.40);
			string cerebro40 = OuvidoPor(ouvinte, tro, Protocol.Fala.Diz, DoGingo);
			string cerebroOoc = OuvidoPor(ouvinte, tro, Protocol.Fala.Ooc, DoGingo);
			string cerebroSussurro = OuvidoPor(ouvinte, tro, Protocol.Fala.Sussurro, DoGingo);
			Ferir(tro, "Torso", 0.25);
			tro.Combate.Nocautear(60, porVital: true);
			string cerebroCaido = OuvidoPor(ouvinte, tro, Protocol.Fala.Diz, DoGingo);
			tro.Combate.Levantar();
			Inteiro(tro);
			string semSentido = FalaSemSentido.Embaralhar(DoGingo, NomesQueAFalaGuarda());
			AfirmarCb("C10. (o pedido 127) o CEREBRO a 40% troca as palavras da fala de boca (say e sussurro), de pe e caido -- caido, gagueja por cima; a 41% e no OOC, igual",
					  cerebro41 == DoGingo && cerebroOoc == DoGingo && cerebro40 != DoGingo && cerebro40 == semSentido
					  && cerebro40.StartsWith("Hey, Nova, How's the ") && cerebroSussurro == semSentido
					  && cerebroCaido == Gagueira.Gaguejar(semSentido, 0.25),
					  $"41% \"{cerebro41}\" | 40% \"{cerebro40}\" | OOC \"{cerebroOoc}\" | sussurro \"{cerebroSussurro}\" | caido \"{cerebroCaido}\"");

			// ---- C11) a fala sem sentido em si
			string en1 = FalaSemSentido.Embaralhar(DoGingo, ["Nova"]), en2 = FalaSemSentido.Embaralhar(DoGingo, ["Nova"]);
			string[] enPal = en1.Split(' ');
			const string EmPortugues = "Oi, você vai treinar comigo amanhã?";
			string pt = FalaSemSentido.Embaralhar(EmPortugues);
			string[] ptPal = pt.Split(' ');
			string gritado = FalaSemSentido.Embaralhar("VOU TREINAR 100 VEZES!!!");
			AfirmarCb("C11. a fala sem sentido: ficam a ligacao e o nome (\"Hey, Nova, How's the\"), a troca tem o jeito do fim (\"-ing\" por \"-ing\") e a caixa, sai igual duas vezes; portugues troca por portugues (\"treinar\" por outro verbo); numero e pontuacao passam; 40% confunde e 41% nao",
					  en1 == en2 && FalaSemSentido.FraseEmIngles(DoGingo) && en1.StartsWith("Hey, Nova, How's the ") && enPal.Length == 6
					  && enPal[4].EndsWith("ing") && enPal[4] != "training" && char.IsLower(enPal[4][0])
					  && enPal[5].EndsWith("ing?") && enPal[5] != "Going?" && char.IsUpper(enPal[5][0])
					  && !FalaSemSentido.FraseEmIngles(EmPortugues) && pt.StartsWith("Oi, você vai ") && pt.Contains(" comigo ") && pt.EndsWith("?")
					  && ptPal.Length == 6 && ptPal[3] != "treinar" && (ptPal[3].EndsWith("ar") || ptPal[3].EndsWith("er") || ptPal[3].EndsWith("ir"))
					  && gritado.StartsWith("VOU ") && gritado.Contains(" 100 ") && gritado.EndsWith("!!!") && gritado == gritado.ToUpperInvariant()
					  && !gritado.Contains("TREINAR") && FalaSemSentido.Confunde(0.40) && !FalaSemSentido.Confunde(0.41),
					  $"\"{en1}\" | \"{pt}\" | \"{gritado}\"");
		}
		finally
		{
			foreach (ServerPlayer p in corpos)
			{
				_players.Remove(p.Id);
				ZoneList(zona.Hash).Remove(p);
				_ultimaFala.Remove(p.Id);
			}
		}
		GD.Print($"[cabeca] ================ {_cbOk} passaram, {_cbFalhou} falharam ================");
	}

	/// <summary>Poe este membro nesta fracao de vida.</summary>
	private static void Ferir(ServerPlayer p, string membro, double fracao)
	{
		if (p.Combate.Corpo.Achar(membro) is { } m) m.Vida = m.VidaMax * fracao;
	}

	/// <summary>Todo membro inteiro de novo, e o corpo de pe.</summary>
	private static void Inteiro(ServerPlayer p)
	{
		foreach (BodyPart m in p.Combate.Corpo.Partes) m.Vida = m.VidaMax;
		if (p.Ficha.KO) p.Combate.Levantar();
	}

	/// <summary>
	/// QUANTOS SEGUNDOS ATE LEVANTAR SOZINHO, em tiques de 0,05 s pelo `Tick` do combate. Com <paramref name="saraAos"/>, o membro volta a
	/// metade da vida nesse segundo -- a cura do nucleo, sem depender do ritmo de regeneracao de cada raca.
	/// </summary>
	private static double Cronometrar(ServerPlayer p, double teto, double saraAos = -1, string membro = "")
	{
		const double Passo = 0.05;
		for (double t = 0; t < teto; t += Passo)
		{
			if (saraAos >= 0 && t >= saraAos && p.Combate.Corpo.Achar(membro) is { } m && m.Fracao < 0.5) m.Vida = m.VidaMax * 0.5;
			p.Combate.Tick(Passo);
			if (!p.Ficha.KO) return t + Passo;
		}
		return double.PositiveInfinity;
	}

	/// <summary>O que <paramref name="ouvinte"/> RECEBEU desta fala digitada de <paramref name="quem"/> (vazio = nada chegou).</summary>
	private string OuvidoPor(ServerPlayer ouvinte, ServerPlayer quem, Protocol.Fala canal, string texto)
	{
		var entregas = new List<(int Para, string Autor, string Texto)>();
		EscutaDeEntregas = entregas;
		try
		{
			_ultimaFala.Remove(quem.Id);
			Falar(quem, canal, texto, digitada: true);
		}
		finally { EscutaDeEntregas = null; }
		return entregas.LastOrDefault(e => e.Para == ouvinte.Id).Texto ?? "";
	}
}
