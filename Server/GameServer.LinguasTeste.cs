using Godot;
using Jandirus.Core.Races;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--linguasteste` (2026-10-27) -- os pedidos 118 e 121 do dono: as linguas do say e as esferas de Namek. Roda no BOOT, com corpos
/// de PESSOA forjados (conta e slot: `EhPessoa`) lado a lado, e mede pela porta de producao -- o verbo do botao (`lingua_falar:`), a fala
/// digitada (`Falar`) e o que cada ouvinte RECEBEU (`EscutaDeEntregas`, depois do embaralhador):
///   L1  o Saiyajin troca o say pra lingua saiyajin pelo verbo do botao;
///   L2  o Humano (0%) le a fala EMBARALHADA, sem nenhuma palavra inteira -- e o outro Saiyajin le inteira; (2026-11-01, o pedido 221) as
///       duas SEM a etiqueta da lingua;
///   L3  ouvir ensina: o Humano sobe +0,05% (o ganho de uma fala);
///   L4  na Galactica todo mundo le inteiro, sem etiqueta;
///   L5  com 100% de maestria o Humano le inteiro, e com 50% le uma parte;
///   L6  o ENSINO: o Saiyajin oferece a lingua, o Humano aceita -- +10% na hora, e passa a aprender 2x mais rapido;
///   L7  a lingua dos DEUSES se aprende 3x mais devagar; as nativas: o meio-Saiyajin sabe as duas, o Androide fala a humana;
///   L8  (121) as esferas de Namek recusam quem sabe 70% da lingua namekuseijin, com a frase -- e com 75% essa frase nao sai.
///   L9  (170) os cargos divinos falam todas as linguas.
///   L10 (221) o HABITANTE fala a nativa dele: a fala automatica de um habitante saiyajin chega embaralhada ao Humano (que aprende) e inteira
///       ao Saiyajin; o mesmo corpo como chefe de saga, ou como alma do Outro Mundo, fala a Galactica.
///
///     Godot --headless --path . --server --port 11731 --linguasteste
/// </summary>
public sealed partial class GameServer
{
	private int _lgOk, _lgFalhou;
	private const int IdBaseDasLinguasDeTeste = 93_400;

	private void AfirmarLg(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _lgOk++; GD.Print($"[linguas]   OK    {oque}"); return; }
		_lgFalhou++;
		GD.PrintErr($"[linguas]   FALHA {oque}   {detalhe}");
	}

	public void RodarBancadaDasLinguas()
	{
		_lgOk = _lgFalhou = 0;
		GD.Print("[linguas] ================ AS LINGUAS DO SAY E AS ESFERAS DE NAMEK (dono, 2026-10-27) ================");

		ZoneKey zona = ZoneKey.Premade("Namek");
		var corpos = new List<ServerPlayer>();
		ServerPlayer Forjar(int i, string nome, string raca, float tilesX)
		{
			var p = new ServerPlayer
			{
				Id = IdBaseDasLinguasDeTeste + i, Peer = null, Name = nome, Race = raca, Genero = "Male", Idade = 25, Zone = zona,
				Pos = new Vec2(tilesX * ZoneCollision.TileSize, 0), Conta = $"bancada_linguas_{i}", Slot = 0,
				Ficha = new Jandirus.Core.Stats.Fighter { Race = raca, BP = 10_000 }, Livro = new Jandirus.Core.Skills.SkillBook(),
			};
			p.Ficha.Class = "Normal";
			PorNoMundo(p);
			corpos.Add(p);
			return p;
		}

		ServerPlayer sai = Forjar(1, "bancada: o saiyajin", "Saiyan", 0);
		ServerPlayer hum = Forjar(2, "bancada: o humano", "Human", 1);
		ServerPlayer sai2 = Forjar(3, "bancada: o outro saiyajin", "Saiyan", 2);
		const string Frase = "o planeta inteiro ouviu o grito do guerreiro";

		// O QUE CADA UM RECEBEU desta fala (o `Mandar` registra por ouvinte, depois do embaralhador). `digitada` falso = a fala do jogo (a do NPC).
		Dictionary<int, string> Dizer(ServerPlayer quem, string texto, bool digitada = true)
		{
			var entregas = new List<(int Para, string Autor, string Texto)>();
			EscutaDeEntregas = entregas;
			try
			{
				_ultimaFala.Remove(quem.Id);
				Falar(quem, Protocol.Fala.Diz, texto, digitada);
			}
			finally { EscutaDeEntregas = null; }
			return entregas.GroupBy(e => e.Para).ToDictionary(g => g.Key, g => g.Last().Texto);
		}

		try
		{
			// ---- L1) o botao
			UsarVerboDeLingua(sai, VerboDaLinguaFalar + "Saiyan");
			AfirmarLg("L1. o verbo do botao troca o say do Saiyajin pra lingua saiyajin (a nativa dele)", LinguaDaFala(sai) == "Saiyan", LinguaDaFala(sai));

			// ---- L2/L3) o humano a 0%, o outro saiyajin, e o ganho
			double antes = Linguas.Maestria(hum.Ficha, "Saiyan");
			var rec = Dizer(sai, Frase);
			string doHumano = rec.GetValueOrDefault(hum.Id, ""), doSaiyajin = rec.GetValueOrDefault(sai2.Id, "");
			bool semPalavraInteira = Frase.Split(' ').Where(w => w.Length > 2).All(w => !doHumano.Split(' ', '[', ']').Contains(w));
			// (2026-11-01, o pedido 221) SEM A ETIQUETA: *"tira o [lingua falada]"* -- nem o "[língua Saiyajin]" de antes, nem colchete nenhum
			AfirmarLg("L2. o HUMANO (0%) le a fala EMBARALHADA, sem nenhuma palavra inteira -- e SEM a etiqueta [língua Saiyajin] (o pedido 221)",
					  doHumano.Length > 0 && !doHumano.Contains('[') && !doHumano.Contains("língua") && doHumano != Frase && semPalavraInteira, doHumano);
			AfirmarLg("...e o outro SAIYAJIN le inteira, sem a etiqueta", doSaiyajin == Frase, doSaiyajin);
			AfirmarLg("L3. ouvir ensina: o Humano sobe o ganho de uma fala (+0,05%)",
					  Math.Abs(Linguas.Maestria(hum.Ficha, "Saiyan") - antes - Linguas.GanhoPorFala) < 1e-9,
					  $"{antes:0.#####} -> {Linguas.Maestria(hum.Ficha, "Saiyan"):0.#####}");

			// ---- L4) a Galactica
			UsarVerboDeLingua(sai, VerboDaLinguaFalar + Linguas.Galactica);
			rec = Dizer(sai, Frase);
			AfirmarLg("L4. na GALACTICA todo mundo le inteiro, sem etiqueta", rec.GetValueOrDefault(hum.Id) == Frase && rec.GetValueOrDefault(sai2.Id) == Frase,
					  rec.GetValueOrDefault(hum.Id, ""));

			// ---- L5) a maestria inteira e a metade
			UsarVerboDeLingua(sai, VerboDaLinguaFalar + "Saiyan");
			hum.Ficha.Linguas["Saiyan"] = 1;
			string cem = Dizer(sai, Frase).GetValueOrDefault(hum.Id, "");
			hum.Ficha.Linguas["Saiyan"] = 0.5;
			string meio = Dizer(sai, Frase).GetValueOrDefault(hum.Id, "");
			string[] palavras = Frase.Split(' ');
			int inteiras = meio.Split(' ').Count(w => palavras.Contains(w));
			AfirmarLg("L5. com 100% o Humano le inteiro; com 50% le UMA PARTE das palavras (nem todas, nem nenhuma)",
					  cem == Frase && inteiras > 0 && inteiras < palavras.Length, $"100%: {cem} | 50%: {meio} ({inteiras} inteiras)");

			// ---- L6) o ensino
			hum.Ficha.Linguas.Remove("Saiyan");
			hum.Ficha.LinguasEnsinadas.Clear();
			hum.Pos = sai.Pos + new Vec2(ZoneCollision.TileSize, 0);
			sai.Facing = Jandirus.Core.World.Facing.East;
			UsarVerboDeLingua(sai, VerboDaLinguaEnsinar);
			UsarVerboDeLingua(hum, VerboDaLinguaAceitar);
			double ensinada = Linguas.Maestria(hum.Ficha, "Saiyan");
			Linguas.Ouvir(hum.Ficha, "Saiyan");
			double depois = Linguas.Maestria(hum.Ficha, "Saiyan");
			AfirmarLg("L6. o ENSINO: o Saiyajin oferece, o Humano aceita -- +10% na hora, e passa a aprender 2x mais rapido",
					  Math.Abs(ensinada - Linguas.BonusDoEnsino) < 1e-9 && hum.Ficha.LinguasEnsinadas.Contains("Saiyan")
					  && Math.Abs(depois - ensinada - 2 * Linguas.GanhoPorFala) < 1e-9, $"ensinada {ensinada:0.####}, depois de uma fala {depois:0.#####}");

			// ---- L7) os Deuses e as nativas
			double d0 = Linguas.Maestria(hum.Ficha, Linguas.DosDeuses);
			Linguas.Ouvir(hum.Ficha, Linguas.DosDeuses);
			var meioSai = new Jandirus.Core.Stats.Fighter { Race = Jandirus.Core.Forms.Catalogo.RacaMeioSaiyajin };
			var andro = new Jandirus.Core.Stats.Fighter { Race = "Android" };
			AfirmarLg("L7. a lingua dos DEUSES se aprende 3x mais devagar; o meio-Saiyajin sabe a saiyajin e a humana; o Androide fala a humana",
					  Math.Abs(Linguas.Maestria(hum.Ficha, Linguas.DosDeuses) - d0 - Linguas.GanhoPorFala / 3) < 1e-9
					  && Linguas.Maestria(meioSai, "Saiyan") == 1 && Linguas.Maestria(meioSai, "Human") == 1
					  && Linguas.Maestria(andro, "Human") == 1 && Linguas.Maestria(andro, "Android") < 1,
					  $"deuses +{Linguas.Maestria(hum.Ficha, Linguas.DosDeuses) - d0:0.########}");

			// ---- L8) (121) as esferas de Namek
			OAsEsferasDeNamekPedemALingua(hum);

			// ---- L9) (2026-10-29, o pedido 170) os cargos divinos falam todas as linguas
			OsCargosDivinosFalamTodasAsLinguas(hum, sai2);

			// ---- L10) (2026-11-01, o pedido 221) o habitante fala a nativa dele
			Jandirus.Core.Npc.MoldeDeNpc? moldeHabitante = _moldes?.Todos.FirstOrDefault(m => m.Tipo == Jandirus.Core.Npc.TipoDeNpc.Cidadao && !m.EhChefe && !m.Alma);
			Jandirus.Core.Npc.MoldeDeNpc? moldeChefe = _moldes?.Todos.FirstOrDefault(m => m.Tipo == Jandirus.Core.Npc.TipoDeNpc.Chefe);
			Jandirus.Core.Npc.MoldeDeNpc? moldeAlma = _moldes?.Todos.FirstOrDefault(m => m.Tipo == Jandirus.Core.Npc.TipoDeNpc.Cidadao && m.Alma);
			if (moldeHabitante == null || moldeChefe == null || moldeAlma == null)
				AfirmarLg("L10. (montagem) ha moldes de habitante, de chefe e de alma", false,
						  $"habitante {moldeHabitante?.Id}, chefe {moldeChefe?.Id}, alma {moldeAlma?.Id}");
			else
			{
				// um corpo de NPC: sem dono na tela e com papel (`EhNpcDoMundo`) -- entao nao e pessoa, e a fala dele e do jogo (`digitada` falso)
				ServerPlayer hab = Forjar(4, "bancada: o habitante saiyajin", "Saiyan", 3);
				hab.Papel = new Jandirus.Core.Npc.PapelDeNpc(moldeHabitante, 1);
				hum.Ficha.Linguas.Remove("Saiyan");
				hum.Ficha.LinguasEnsinadas.Clear();
				double antesDoHabitante = Linguas.Maestria(hum.Ficha, "Saiyan");
				var doHabitante = Dizer(hab, Frase, digitada: false);
				string humOuviu = doHabitante.GetValueOrDefault(hum.Id, ""), saiOuviu = doHabitante.GetValueOrDefault(sai2.Id, "");
				bool nenhumaInteira = Frase.Split(' ').Where(w => w.Length > 2).All(w => !humOuviu.Split(' ').Contains(w));
				AfirmarLg("L10. (221) **O HABITANTE SAIYAJIN FALA A NATIVA DELE**: a fala do jogo dele chega EMBARALHADA ao Humano (0%), sem etiqueta -- "
						  + "e o Humano aprende ouvindo (+0,05%)",
						  !EhPessoa(hab) && humOuviu.Length > 0 && humOuviu != Frase && nenhumaInteira && !humOuviu.Contains('[')
						  && Math.Abs(Linguas.Maestria(hum.Ficha, "Saiyan") - antesDoHabitante - Linguas.GanhoPorFala) < 1e-9,
						  $"pessoa {EhPessoa(hab)} | o humano: {humOuviu} | {antesDoHabitante:0.#####} -> {Linguas.Maestria(hum.Ficha, "Saiyan"):0.#####}");
				AfirmarLg("...e o SAIYAJIN a le inteira", saiOuviu == Frase, saiOuviu);

				hab.Papel = new Jandirus.Core.Npc.PapelDeNpc(moldeChefe, 1);
				string doChefe = Dizer(hab, Frase, digitada: false).GetValueOrDefault(hum.Id, "");
				hab.Papel = new Jandirus.Core.Npc.PapelDeNpc(moldeAlma, 1);
				string daAlma = Dizer(hab, Frase, digitada: false).GetValueOrDefault(hum.Id, "");
				AfirmarLg("L10. ...o mesmo corpo como CHEFE DE SAGA, ou como ALMA do Outro Mundo, fala a Galactica: o Humano le inteiro",
						  doChefe == Frase && daAlma == Frase, $"chefe ({moldeChefe.Id}): {doChefe} | alma ({moldeAlma.Id}): {daAlma}");
				hum.Ficha.Linguas.Remove("Saiyan");
			}
		}
		catch (Exception e) { AfirmarLg($"a bancada rodou inteira (estourou: {e.Message})", false, e.StackTrace ?? ""); }
		finally
		{
			LinguaDeNamekNaBancadaDeTeste = false;
			foreach (ServerPlayer p in corpos)
			{
				_linguaDaFala.Remove(p.Id);
				_licaoDeLingua.Remove(p.Id);
				_players.Remove(p.Id);
				ZoneList(zona.Hash).Remove(p);
			}
		}
		GD.Print($"[linguas] ================ {_lgOk} passaram, {_lgFalhou} falharam ================");
	}

	/// <summary>
	/// L9 (2026-10-29, o pedido 170): pela porta de producao (`ConferirALingua`, a do login e a da coroacao), o Kaio do Norte e o Deus da
	/// Destruicao passam a falar TODAS as linguas do universo a 100% (a de cada raca e a dos Deuses); o Rei Yemma (cargo divino, mas nao e
	/// Kaio) e quem nao tem cargo, nao.
	/// </summary>
	private void OsCargosDivinosFalamTodasAsLinguas(ServerPlayer kaio, ServerPlayer outro)
	{
		var antes = new Dictionary<string, string>(_tronos, StringComparer.OrdinalIgnoreCase);
		try
		{
			string[] todas = [.. LinguasDoUniverso()];
			string[] Faltam(ServerPlayer p) => [.. todas.Where(l => !Linguas.PodeFalar(p.Ficha, l))];

			kaio.Ficha.Linguas.Clear();
			outro.Ficha.Linguas.Clear();
			ConferirALingua(outro);
			AfirmarLg("L9. (montagem) sem cargo, o corpo NAO fala a lingua namekuseijin -- e as linguas do universo sao a de cada raca e a dos Deuses",
					  !Linguas.PodeFalar(outro.Ficha, "Namekian") && todas.Length > 5 && todas.Contains("Namekian") && todas.Contains(Linguas.DosDeuses),
					  $"{todas.Length}: {string.Join(", ", todas)}");

			_tronos["nkai"] = kaio.Conta;
			ConferirALingua(kaio);
			string[] doKaio = Faltam(kaio);
			_tronos.Remove("nkai");
			_tronos["godofdestruction"] = outro.Conta;
			ConferirALingua(outro);
			string[] doDeus = Faltam(outro);
			AfirmarLg($"L9. (170) **O KAIO DO NORTE E O DEUS DA DESTRUICAO FALAM TODAS AS {todas.Length} LINGUAS DO UNIVERSO A 100%** (*\"saiba falar "
					  + "todas as linguas do unvierso em 100%\"*), pelo login/coroacao", doKaio.Length == 0 && doDeus.Length == 0,
					  $"faltam ao Kaio: {string.Join(", ", doKaio)} | ao Deus: {string.Join(", ", doDeus)}");

			_tronos.Remove("godofdestruction");
			outro.Ficha.Linguas.Clear();
			_tronos["yemma"] = outro.Conta;
			ConferirALingua(outro);
			AfirmarLg("...e o REI YEMMA (cargo divino que nao e Kaio) continua sem a namekuseijin", !Linguas.PodeFalar(outro.Ficha, "Namekian"),
					  $"{Linguas.Maestria(outro.Ficha, "Namekian"):0.##}");
		}
		finally
		{
			_tronos.Clear();
			foreach ((string k, string v) in antes) _tronos[k] = v;
		}
	}

	/// <summary>L8: com as sete do set de Namek nas maos, 70% da lingua namekuseijin e recusado com a frase; com 75% a frase nao sai.</summary>
	private void OAsEsferasDeNamekPedemALingua(ServerPlayer pl)
	{
		SetDeEsferas? deNamek = _sets.Find(s => s.ZonaNome == "Namek");
		if (deNamek == null) { AfirmarLg("L8. (montagem) ha um set de esferas em Namek", false); return; }
		List<Esfera> sete = [.. _esferas.Where(e => e.Set == deNamek.Id)];
		var portadores = sete.ToDictionary(e => e, e => e.Portador);
		bool tinhaInvocacao = _invocacoes.ContainsKey(deNamek.Id);
		LinguaDeNamekNaBancadaDeTeste = true;
		try
		{
			foreach (Esfera e in sete) e.Portador = pl.Id;
			pl.Ficha.Linguas["Namekian"] = 0.70;
			List<string> com70 = Ouvir(() => InvocarODragao(pl));
			pl.Ficha.Linguas["Namekian"] = 0.75;
			List<string> com75 = Ouvir(() => InvocarODragao(pl));
			AfirmarLg("L8. (121) AS ESFERAS DE NAMEK pedem 75% da lingua: com 70% recusam, com a frase; com 75% essa frase nao sai",
					  sete.Count == Jandirus.Core.Magic.Esferas.Total
					  && com70.Any(s => s.Contains("só respondem a quem fala a língua dos Namekuseijins") && s.Contains("75%"))
					  && !com75.Any(s => s.Contains("só respondem a quem fala a língua dos Namekuseijins")),
					  $"70%: {string.Join(" | ", com70)} || 75%: {string.Join(" | ", com75)}");
		}
		finally
		{
			LinguaDeNamekNaBancadaDeTeste = false;
			foreach ((Esfera e, int p) in portadores) e.Portador = p;
			if (!tinhaInvocacao) _invocacoes.Remove(deNamek.Id);
			pl.Ficha.Linguas.Remove("Namekian");
		}
	}
}
