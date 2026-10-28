using Godot;
using Jandirus.Core.Npc;
using Jandirus.Core.Social;
using Jandirus.Core.Stats;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--mentelistateste` (2026-10-27) -- o pedido 124c, a MEDITACAO PROFUNDA NOVA. Corpos forjados (conta, sem `Peer`) com os ganchos de
/// combate de producao (`PrepararCombate`); a escolha entra pelo canal de habilidade de verdade (`UsarHabilidade`). A porta da mente, que
/// exige um cliente, e o robo `--diagmentelista` quem atravessa (a lista abre, e nao ha reflexo automatico).
///   ML1 QUEM SE CONHECE: o habitante visto de perto entra pelo molde e pelo planeta (o bicho nao); o jogador fotografado leva o poder e a
///       cara; a lista tem as quatro categorias, na ordem, e a foto vem do servidor so pros habitantes e chefes; e so onde o habitante pode
///       nascer (o cidadao visto no espaco nao entra, e a chave velha de um lugar sem berco some sem derrubar a lista);
///   ML2 A ESCOLHA: o reflexo nasce pela lista e a tela fecha; com ele de pe outra escolha e recusada, e acordar tambem;
///   ML3 A VITORIA: derrubar a lembranca a desfaz, paga BP e devolve a lista;
///   ML4 O BP PELA FORCA: a lembranca de um jogador muito mais forte nasce com a raca e o poder dele, e paga na proporcao da forca;
///   ML5 A DERROTA: cair com a lembranca de pe acorda e recusa o mergulho por 30 min -- passado o prazo, nao;
///   ML6 A LISTA FECHA na saida, por qualquer porta, e volta pro dono que ficou sozinho quando a visita sai;
///   ML7 A DERROTA PELA BORDA: em jogo quem cai e tirado pela borda do corpo largado (antes do tique da lembranca) -- a mesma marca.
///
///     Godot --headless --path . --server --port 11881 --mentelistateste
/// </summary>
public sealed partial class GameServer
{
	private int _mlOk, _mlFalhou;

	private void AfirmarMl(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _mlOk++; GD.Print($"[mentelista]   OK    {oque}   [{detalhe}]"); return; }
		_mlFalhou++;
		GD.PrintErr($"[mentelista]   FALHA {oque}   {detalhe}");
	}

	public void RodarBancadaDaListaDaMente()
	{
		_mlOk = _mlFalhou = 0;
		GD.Print("[mentelista] ================ A MEDITACAO PROFUNDA NOVA (o pedido 124c, 2026-10-27) ================");
		const int idBase = 992_900;
		const float t = ZoneCollision.TileSize;
		var forjados = new List<ServerPlayer>();
		var nascidos = new List<ServerPlayer>();
		ZoneKey terra = ZoneKey.Premade("Earth");
		ServerPlayer Forjar(int i, string nome, double bp, string raca = "Human", ZoneKey? zona = null, Vec2? pos = null)
		{
			var p = new ServerPlayer
			{
				Id = idBase + i, Peer = null, Name = nome, Race = raca, Genero = "Male", Idade = 25, Zone = zona ?? terra,
				Pos = pos ?? new Vec2(i * 40 * t, 3 * t), Conta = $"bancada_mentelista_{i}", Slot = 0,
				Ficha = new Fighter { Race = raca, BP = bp }, Livro = new Jandirus.Core.Skills.SkillBook(),
			};
			p.Ficha.Class = "Normal";
			PorNoMundo(p);
			PrepararCombate(p, null);
			p.Ficha.PowerLevel();
			p.Ficha.Ki = p.Ficha.MaxKi;
			forjados.Add(p);
			return p;
		}
		void UmTique()
		{
			TickCombate(Jandirus.Net.Protocol.TickSeconds);
			TickDosCorposSemDono(Jandirus.Net.Protocol.TickSeconds);
		}
		List<string> Escolher(ServerPlayer p, MenteOponentes.Tipo tipo, string id) =>
			Ouvir(() => UsarHabilidade(p, MenteOponentes.Pedido(tipo, id)));
		(bool Abrir, int Linhas)? UltimaLista(ServerPlayer p) =>
			EscutaDaListaDaMente!.LastOrDefault(e => e.Quem == p.Id) is { Quem: > 0 } e ? (e.Abrir, e.Linhas.Count) : null;
		var escutaAntes = EscutaDaListaDaMente;
		EscutaDaListaDaMente = [];
		try
		{
			// ---------------------------------------------------------------- ML1) quem se conhece
			MoldeDeNpc? cidadao = _moldes?.Todos.FirstOrDefault(m => m.Tipo == TipoDeNpc.Cidadao && !m.EhChefe);
			MoldeDeNpc? bicho = _moldes?.Todos.FirstOrDefault(m => m.Tipo == TipoDeNpc.Animal);
			MoldeDeNpc? chefe = _moldes?.Todos.FirstOrDefault(m => m.EhChefe && !BillsEWhis.EhMolde(m.Id));
			string chaveDoCidadao = cidadao != null ? MenteOponentes.ChaveDeHabitante(cidadao.Id, terra.Name) : "";
			ServerPlayer dono = Forjar(1, "bancada: quem medita", 10_000);
			ServerPlayer amigo = Forjar(2, "bancada: o amigo", 40_000, "Namekian");
			amigo.Pos = dono.Pos + new Vec2(2 * t, 0);
			ServerPlayer? habitante = cidadao != null ? NascerNpc(cidadao.Id, terra, dono.Pos + new Vec2(3 * t, 0), 9_929_001) : null;
			ServerPlayer? fera = bicho != null ? NascerNpc(bicho.Id, terra, dono.Pos + new Vec2(0, 3 * t), 9_929_002) : null;
			if (habitante != null) nascidos.Add(habitante);
			if (fera != null) nascidos.Add(fera);
			AnotarHabitantesVistos();
			HashSet<string> vistos = dono.Ficha.HabitantesDaMente ?? [];
			Fotografar(dono, amigo, NowMs());
			dono.Social.Conhecidos.TryGetValue(amigo.Assinatura, out Conhecido? foto);
			if (chefe != null) dono.ChefesVistos.Add(chefe.Id);
			List<OponenteDaMente> lista = MontarAListaDaMente(dono);
			MenteOponentes.Tipo[] ordem = [.. lista.Select(o => o.Tipo).Distinct()];
			AfirmarMl("ML1. QUEM SE CONHECE: o habitante visto de perto entra pelo molde e pelo planeta (o bicho nao), o jogador fotografado leva o poder e a cara",
					  cidadao != null && vistos.Contains(chaveDoCidadao) && (bicho == null || !vistos.Any(v => v.StartsWith(bicho.Id + "@", StringComparison.Ordinal)))
					  && foto is { PoderVisto: > 0, AparenciaVista: not null } && Math.Abs(foto.PoderVisto - amigo.Ficha.expressedBP) < 1,
					  $"vistos [{string.Join(", ", vistos)}]; poder visto {foto?.PoderVisto:0} (o do amigo {amigo.Ficha.expressedBP:0}), cara {foto?.AparenciaVista != null}");
			AfirmarMl("ML1b. ...e a lista tem as QUATRO categorias na ordem (voce, habitantes, jogadores, chefes), com a foto do servidor em todos menos no reflexo (o jogador, a cara de quando foi visto)",
					  ordem.SequenceEqual([MenteOponentes.Tipo.Reflexo, MenteOponentes.Tipo.Habitante, MenteOponentes.Tipo.Jogador, MenteOponentes.Tipo.Chefe])
					  && lista.All(o => (o.Foto != null) == (o.Tipo != MenteOponentes.Tipo.Reflexo))
					  && lista.Any(o => o.Tipo == MenteOponentes.Tipo.Jogador && o.Id == amigo.Assinatura),
					  string.Join(" | ", lista.Select(o => $"{o.Tipo}:{o.Nome}{(o.Foto != null ? "+foto" : "")}")));
			// (2026-10-27) O PLANETA DO HABITANTE E O QUE O JOGADOR LE: "Terra", e nao o "Earth" do mapa (a foto da rodada r4 do `mentelista`
			// mostrava "Habitante (Earth)") -- o `NomeDoPlaneta` do resto do jogo, que tambem traz o nome batizado.
			string nomeDoCidadao = lista.Where(o => o.Tipo == MenteOponentes.Tipo.Habitante && o.Id == chaveDoCidadao).Select(o => o.Nome).FirstOrDefault() ?? "(nenhum)";
			AfirmarMl("ML1d. o habitante da Terra aparece como \"(Terra)\" na lista -- o nome que o jogador le, e nao o \"Earth\" do mapa",
					  nomeDoCidadao.EndsWith("(Terra)", StringComparison.Ordinal), nomeDoCidadao);

			// ---------------------------------------------------------------- ML1c) so onde ele pode nascer
			// O cidadao nao tem raca sem o planeta (ela sai do berco dele): o que seguiu alguem pro ESPACO, anotado la, seria uma linha que nao se
			// monta nem nasce (o `Sortear` grita com planeta sem berco). E a chave velha de um lugar sem berco some calada -- nao derruba a lista.
			int habitantesAntes = lista.Count(o => o.Tipo == MenteOponentes.Tipo.Habitante);
			ServerPlayer astronauta = Forjar(3, "bancada: quem esta no espaco", 10_000, zona: ZonaDoEspaco, pos: new Vec2(40 * t, 40 * t));
			ServerPlayer? perdido = cidadao != null ? NascerNpc(cidadao.Id, terra, dono.Pos + new Vec2(6 * t, 0), 9_929_003) : null;
			if (perdido != null) { nascidos.Add(perdido); MoveToZone(perdido.Id, ZonaDoEspaco, astronauta.Pos + new Vec2(t, 0)); }
			AnotarHabitantesVistos();
			if (cidadao != null) (dono.Ficha.HabitantesDaMente ??= []).Add(MenteOponentes.ChaveDeHabitante(cidadao.Id, "Lugar Nenhum"));
			int habitantesComAVelha = MontarAListaDaMente(dono).Count(o => o.Tipo == MenteOponentes.Tipo.Habitante);
			AfirmarMl("ML1c. ...e SO ONDE ELE PODE NASCER: o cidadao visto no espaco nao entra, e a chave velha de um lugar sem berco some da lista sem derruba-la",
					  perdido?.Zone.Equals(ZonaDoEspaco) == true && astronauta.Ficha.HabitantesDaMente is not { Count: > 0 } && habitantesComAVelha == habitantesAntes,
					  $"no espaco {perdido?.Zone.Equals(ZonaDoEspaco)}; vistos no espaco [{string.Join(", ", astronauta.Ficha.HabitantesDaMente ?? [])}]; "
					  + $"habitantes na lista {habitantesComAVelha} (antes {habitantesAntes})");

			// ---------------------------------------------------------------- ML2) a escolha
			ZoneKey mente = DimensaoMental.De(idBase + 10);
			ServerPlayer sonhador = Forjar(10, "bancada: o dono da mente", 10_000, zona: mente, pos: PosMental);
			Fotografar(sonhador, amigo, NowMs());
			if (cidadao != null) sonhador.Ficha.HabitantesDaMente = [chaveDoCidadao];
			EscutaDaListaDaMente.Clear();
			List<string> escolhido = Escolher(sonhador, MenteOponentes.Tipo.Reflexo, "");
			int reflexoId = sonhador.CloneId;
			bool nasceu = _players.TryGetValue(reflexoId, out ServerPlayer? reflexo) && reflexo.DonoDoClone == sonhador.Id && reflexo.Zone.Equals(mente);
			bool fechou = UltimaLista(sonhador) is { Abrir: false };
			List<string> outra = Escolher(sonhador, MenteOponentes.Tipo.Habitante, chaveDoCidadao);
			List<string> acordar = Ouvir(() => SairDoTranse(sonhador));
			AfirmarMl("ML2. A ESCOLHA: o reflexo nasce pela lista (o canal de habilidade) e a tela fecha; com ele de pe, outra escolha e recusada, e ACORDAR tambem",
					  nasceu && fechou && sonhador.CloneId == reflexoId && outra.Any(s => s.Contains("ainda nao acabou"))
					  && NaMente(sonhador) && acordar.Any(s => s.Contains("so acaba quando um dos dois cai")),
					  $"nasceu {nasceu} ({reflexoId}), fechou {fechou}; outra: {string.Join(" | ", outra)}; acordar: {string.Join(" | ", acordar)}; {string.Join(" | ", escolhido)}");

			// ---------------------------------------------------------------- ML3) a vitoria
			double fatorDoReflexo = reflexo != null ? MenteOponentes.Fator(reflexo.Ficha.expressedBP, sonhador.Ficha.expressedBP) : 0;
			double antes = sonhador.Ficha.BP;
			EscutaDaListaDaMente.Clear();
			reflexo?.Combate.Nocautear(10);
			List<string> venceu = Ouvir(UmTique);
			double ganhoDoReflexo = sonhador.Ficha.BP - antes;
			AfirmarMl("ML3. A VITORIA: derrubar a lembranca a DESFAZ (nao se reergue), paga BP e devolve a lista",
					  sonhador.CloneId == 0 && !_players.ContainsKey(reflexoId) && ganhoDoReflexo > 0 && UltimaLista(sonhador) is { Abrir: true }
					  && venceu.Any(s => s.Contains("VITORIA")) && NaMente(sonhador),
					  $"lembranca {sonhador.CloneId}, +{ganhoDoReflexo:0.####} BP (fator {fatorDoReflexo:0.##}); {string.Join(" | ", venceu)}");

			// ---------------------------------------------------------------- ML4) o BP pela forca
			if (sonhador.Social.Conhecidos.TryGetValue(amigo.Assinatura, out Conhecido? doAmigo))
				doAmigo.PoderVisto = sonhador.Ficha.expressedBP * 3;
			Escolher(sonhador, MenteOponentes.Tipo.Jogador, amigo.Assinatura);
			bool comoEle = _players.TryGetValue(sonhador.CloneId, out ServerPlayer? lembranca) && lembranca.Race == "Namekian"
						   && lembranca.Name.StartsWith(amigo.Name) && Math.Abs(lembranca.BpDaMente - sonhador.Ficha.expressedBP * 3) < 1;
			double fatorDele = lembranca != null ? MenteOponentes.Fator(lembranca.Ficha.expressedBP, sonhador.Ficha.expressedBP) : 0;
			double antes4 = sonhador.Ficha.BP;
			lembranca?.Combate.Nocautear(10);
			UmTique();
			double ganhoDele = sonhador.Ficha.BP - antes4;
			double razao = ganhoDoReflexo > 0 ? ganhoDele / ganhoDoReflexo : 0, esperada = fatorDoReflexo > 0 ? fatorDele / fatorDoReflexo : 0;
			AfirmarMl("ML4. O BP PELA FORCA: a lembranca do jogador nasce com a raca e o poder de quando foi vista, e paga na proporcao da forca dela",
					  comoEle && fatorDele > fatorDoReflexo && Math.Abs(razao - esperada) <= 0.15 * esperada,
					  $"como ele {comoEle}; fator {fatorDele:0.##} contra {fatorDoReflexo:0.##}; ganho {ganhoDele:0.####} contra {ganhoDoReflexo:0.####} (razao {razao:0.##}, esperada {esperada:0.##})");

			// ---------------------------------------------------------------- ML5) a derrota
			Escolher(sonhador, MenteOponentes.Tipo.Habitante, chaveDoCidadao);
			int habitanteId = sonhador.CloneId;
			// o cidadao da TERRA nasce humano na mente: a raca e do planeta onde foi visto, e nao da zona da mente (que nao e berco)
			bool habitanteNasceu = _players.TryGetValue(habitanteId, out ServerPlayer? sombra) && sombra.Papel?.Molde.Id == cidadao?.Id
								   && sombra.Race == "Human";
			sonhador.Combate.Nocautear(30);
			List<string> perdeu = Ouvir(UmTique);
			bool bloqueado = sonhador.Ficha.mergulho_bloqueado_ate > NowMs() + 29 * 60_000;
			// A VOLTA E A DA ONDA (1,8 s de relogio): o prazo vence NA MAO -- o recurso das outras bancadas sincronas (a `--menteviva` vence a
			// graca do Oozaru assim) -- e o corpo levanta. So fora da mente e de pe a recusa que sobra e a do foco (as do Core vem antes dela).
			for (int i = 0; i < _ondaDaMente.Count; i++)
				if (_ondaDaMente[i].Id == sonhador.Id) _ondaDaMente[i] = _ondaDaMente[i] with { Quando = 0 };
			TickDaOndaDaMente();
			sonhador.Combate.Levantar();
			bool acordou = !NaMente(sonhador);
			string recusa = RecusaDoMergulho(sonhador);
			sonhador.Ficha.mergulho_bloqueado_ate = NowMs() - 1;
			string depois = RecusaDoMergulho(sonhador);
			AfirmarMl("ML5. A DERROTA: cair com a lembranca de pe ACORDA e recusa o mergulho por 30 min -- passado o prazo, a recusa do foco some",
					  habitanteNasceu && bloqueado && sonhador.CloneId == 0 && perdeu.Any(s => s.Contains("te derruba")) && acordou
					  && recusa.Contains("abala o seu foco") && !depois.Contains("abala o seu foco"),
					  $"habitante {habitanteNasceu}; bloqueado {bloqueado}; acordou {acordou}; {string.Join(" | ", perdeu)}; recusa: {recusa}; depois: {depois}");

			// ---------------------------------------------------------------- ML6) a lista fecha na saida, e volta pro dono quando a visita sai
			ZoneKey outraMente = DimensaoMental.De(idBase + 20);
			ServerPlayer anfitriao = Forjar(20, "bancada: o anfitriao", 10_000, zona: outraMente, pos: PosMental);
			ServerPlayer visita = Forjar(21, "bancada: a visita", 10_000, zona: outraMente, pos: PosMental + new Vec2(3 * t, 0));
			EscutaDaListaDaMente.Clear();
			SairDaMente(visita, "a bancada acordou a visita.");
			bool voltouProDono = UltimaLista(anfitriao) is { Abrir: true };
			bool fechouPraVisita = UltimaLista(visita) is { Abrir: false };
			SairDaMente(anfitriao, "a bancada acordou o dono.");
			bool fechouProDono = UltimaLista(anfitriao) is { Abrir: false };
			AfirmarMl("ML6. A LISTA FECHA na saida, por qualquer porta, e o dono que fica sozinho a recebe de volta quando a visita sai",
					  voltouProDono && fechouPraVisita && fechouProDono && !NaMente(anfitriao),
					  $"volta pro dono {voltouProDono}, fecha pra visita {fechouPraVisita}, fecha pro dono {fechouProDono}");

			// ---------------------------------------------------------------- ML7) a derrota pela borda (o caminho de jogo)
			// Em jogo quem cai na mente e tirado pela BORDA do corpo largado (`BordasDeQuemEstaFora` -> a fila -> `VoltarDeOndeEstiver`), no
			// `TickCombate` -- antes do tique da lembranca. Corpo forjado nao larga corpo (exige `Peer`), entao a bancada chama a porta que a fila
			// chama; o robo `--diagmentelista` atravessa a corrente inteira com um cliente de verdade.
			ZoneKey mente7 = DimensaoMental.De(idBase + 30);
			ServerPlayer caido = Forjar(30, "bancada: quem cai pela borda", 10_000, zona: mente7, pos: PosMental);
			Escolher(caido, MenteOponentes.Tipo.Reflexo, "");
			bool dePe = LutandoNaMente(caido);
			caido.Combate.Nocautear(30);
			List<string> pelaBorda = Ouvir(() => VoltarDeOndeEstiver(caido, "algo atinge o seu corpo -- voce volta pra ele na hora."));
			AfirmarMl("ML7. ...e a DERROTA tambem pela BORDA do corpo largado (o caminho de jogo): sair caido com a lembranca de pe marca o foco abalado e diz quem derrubou",
					  dePe && !NaMente(caido) && caido.Ficha.mergulho_bloqueado_ate > NowMs() + 29 * 60_000 && pelaBorda.Any(s => s.Contains("te derruba")),
					  $"de pe {dePe}; na mente {NaMente(caido)}; {string.Join(" | ", pelaBorda)}");
		}
		catch (Exception e) { AfirmarMl("a bancada rodou ate o fim", false, e.ToString()); }
		finally
		{
			EscutaDaListaDaMente = escutaAntes;
			foreach (ServerPlayer p in forjados)
			{
				DesfazerOOponente(p, "");
				_players.Remove(p.Id);
				ZoneList(p.Zone.Hash).Remove(p);
			}
			foreach (ServerPlayer n in nascidos) if (_players.ContainsKey(n.Id)) RemoverNpc(n);
		}
		GD.Print($"[mentelista] ================ {_mlOk} passaram, {_mlFalhou} falharam ================");
	}
}
