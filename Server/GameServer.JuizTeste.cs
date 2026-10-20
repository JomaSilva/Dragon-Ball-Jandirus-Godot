using System.Text.Json;
using Godot;
using Jandirus.Core.Stats;
using Jandirus.Core.Verbos;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--juizteste` -- O KING YEMMA VAI E VOLTA DO OUTRO MUNDO (dono, 2026-10-19). O pedido e as decisoes estao no cabecalho de
/// `GameServer.TravessiaDoJuiz.cs`. Corpos forjados (sem tela), e tudo pelas portas de producao: o canal de habilidade
/// (`UsarHabilidade`), o `Revive` do kit (`RessuscitarG4`) e o carimbo (`JulgarG8`).
///
///   1. A TABELA: o botao e do King Yemma -- ele o ve e o comum nao.
///   2. A RECUSA: o comum aperta e ouve "so o King Yemma", e fica onde estava.
///   3. A IDA: o juiz chega na mesa do Enma, a ficha guarda o lugar EXATO de onde ele saiu, e o fato da volta acende.
///   4. O REVIVE LA DENTRO: a alma volta a vida e acorda no mundo dos vivos, em casa -- CONTRA-EXEMPLO `RevividoFicaNoAlemDeTeste` (ela
///      fica viva no Outro Mundo).
///   5. A VOLTA: CONTRA-EXEMPLO `VoltaDoJuizPeloBercoDeTeste` (cai no berco); sem ele, o lugar EXATO -- e a volta e gasta.
///   6. O CARIMBO guarda a volta: do Ceu, a travessia devolve ao ponto de onde ele bateu o carimbo.
///   7. O TRONO PERDIDO LA DENTRO: o botao fica (o fato), a volta acontece; de volta, sem trono, a ida e recusada.
///   8. O LUGAR QUE NAO EXISTE MAIS devolve ao berco, com a frase.
///   9. AS PORTAS: caido nao atravessa (o morto no chao dos vivos e cadaver, e cai nesta mesma), o fantasma no Outro Mundo nao sai por
///      ela, e da Sala do Tempo nao se sai por ela.
///  10. O DISCO: a volta viaja com a ficha (o juiz que sai do jogo do lado de la volta pro mesmo lugar).
///
/// CADA PASSO COMECA DE UM LUGAR CONHECIDO (`PorEm`: o lugar, de pe, vivo, sem a recarga do salto): o defeito de um passo nao arrasta
/// os seguintes, e a contraprova por mutacao derruba so a afirmacao da linha que tirou.
///
///     Godot --headless --path . --server --port 8761 --juizteste
/// </summary>
public sealed partial class GameServer
{
	private bool _juizJaRodou;
	private int _jzOk, _jzFalhou;

	/// <summary>O comeco dos ids dos corpos desta bancada -- fora da faixa dos de jogo e das outras bancadas.</summary>
	private const int IdBaseDoJuizDeTeste = 987_760;

	private void AfirmarJz(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _jzOk++; GD.Print($"[juiz]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _jzFalhou++; GD.PrintErr($"[juiz]   FALHA {nome}   [{detalhe}]"); }
	}

	public void RodarBancadaDoJuiz()
	{
		if (_juizJaRodou) return;
		_juizJaRodou = true;
		_jzOk = _jzFalhou = 0;
		GD.Print("[juiz] ================ O KING YEMMA VAI E VOLTA DO OUTRO MUNDO (dono, 2026-10-19) ================");

		var forjados = new List<ServerPlayer>();
		string? tronoAntes = _tronos.GetValueOrDefault(TravessiaDoJuiz.Cargo);
		try
		{
			RoteiroDoJuiz(forjados);
		}
		catch (Exception e)
		{
			AfirmarJz("o roteiro correu ate o fim sem excecao", false, $"{e.GetType().Name}: {e.Message}");
			GD.PrintErr(e.ToString());
		}
		finally
		{
			VoltaDoJuizPeloBercoDeTeste = false;
			RevividoFicaNoAlemDeTeste = false;
			foreach (ServerPlayer c in forjados)
				if (_players.ContainsKey(c.Id)) RemoverNpc(c);
			if (tronoAntes == null) _tronos.Remove(TravessiaDoJuiz.Cargo);
			else _tronos[TravessiaDoJuiz.Cargo] = tronoAntes;
			GD.Print($"[juiz] ================ {_jzOk} OK, {_jzFalhou} FALHA(S) ================");
		}
	}

	private void RoteiroDoJuiz(List<ServerPlayer> forjados)
	{
		const float T = ZoneCollision.TileSize;
		ZoneKey terra = SpawnZone;
		ZoneKey alem = ZoneKey.Premade(Alem.ZonaDoOutroMundo);
		Vec2 Livre(ZoneKey z, Vec2 p) => MapaDaZonaOuCatalogo(z)?.PontoLivrePerto(p) ?? p;

		ServerPlayer Forjar(int i, string nome, ZoneKey zona, Vec2 pos)
		{
			var c = new ServerPlayer
			{
				Id = IdBaseDoJuizDeTeste + i, Peer = null, Name = nome, Race = "Human", Genero = "Male", Idade = 30,
				Zone = zona, Pos = pos, Conta = $"bancada_juiz_{i}", Slot = 0,
				Ficha = new Fighter { Race = "Human", BP = 1000 },
				Livro = new Jandirus.Core.Skills.SkillBook(),
			};
			c.Ficha.Class = "Normal";
			PorNoMundo(c);
			c.Ficha.Ki = c.Ficha.MaxKi;
			forjados.Add(c);
			return c;
		}
		// O LUGAR CONHECIDO DE CADA PASSO: vivo, de pe, nesta zona e neste ponto, sem a recarga do salto.
		void PorEm(ServerPlayer quem, ZoneKey z, Vec2 p)
		{
			if (quem.Ficha.dead) { quem.Combate.Reviver(); quem.RelogioDaMorte = 0; }
			if (quem.Ficha.KO) quem.Combate.Curar();
			if (!quem.Zone.Equals(z)) MoveToZone(quem.Id, z, p);
			CravarPosicao(quem, p);
			_saltoPronto.Remove(quem.Id);
		}
		void Apertar(ServerPlayer quem)
		{
			_saltoPronto.Remove(quem.Id);   // a recarga de um segundo de todo salto nao e o que esta bancada mede
			UsarHabilidade(quem, TravessiaDoJuiz.Verbo);
		}
		List<string> ApertarOuvindo(ServerPlayer quem) => Ouvir(() => Apertar(quem));
		bool Em(ServerPlayer quem, ZoneKey z, Vec2 p) => quem.Zone.Equals(z) && Vec2.Distance(quem.Pos, p) < 1f;
		string Onde(ServerPlayer quem) => $"{quem.Zone} ({quem.Pos.X:0.#},{quem.Pos.Y:0.#})";

		Vec2 p1 = Livre(terra, SpawnPos + new Vec2(7.5f * T, 3f * T));
		Vec2 p2 = Livre(terra, SpawnPos + new Vec2(-3.5f * T, -6f * T));
		Vec2 p3 = Livre(terra, SpawnPos + new Vec2(4.5f * T, -5f * T));
		Vec2 p4 = Livre(terra, SpawnPos + new Vec2(9.5f * T, -2f * T));
		Vec2 doComum = Livre(terra, SpawnPos + new Vec2(-6.5f * T, 2f * T));
		Vec2 mesa = MesaDoEnma(alem);
		ServerPlayer juiz = Forjar(0, "Juiz", terra, p1);
		ServerPlayer comum = Forjar(1, "Comum", terra, doComum);
		_tronos[TravessiaDoJuiz.Cargo] = juiz.Conta;
		juiz.Livro!.DarComoEnsinada("/datum/skill/rank/Revive");

		// ---------------------------------------------------------------- 1. a tabela
		VerbDeclarado? linha = RequisitosDosVerbos.Linha(RequisitosDosVerbos.PrefixoDeHabilidade + TravessiaDoJuiz.Verbo);
		bool Ve(ServerPlayer quem) => linha != null && linha.Requisito.Atende(new PosseDoServidor(this, quem));
		AfirmarJz("1. A TABELA: o botao da travessia e do King Yemma -- ele o ve, e quem nao carrega o trono nao", Ve(juiz) && !Ve(comum),
				  linha?.Requisito.Texto ?? "sem linha");

		// ---------------------------------------------------------------- 2. a recusa
		List<string> recusa = ApertarOuvindo(comum);
		AfirmarJz("2. QUEM NAO E O JUIZ NAO ATRAVESSA: a recusa fala do King Yemma, e o corpo fica onde estava",
				  Disse(recusa, "só o King Yemma") && Em(comum, terra, doComum), $"{Ultimos(recusa)} | {Onde(comum)}");

		// ---------------------------------------------------------------- 3. a ida
		PorEm(juiz, terra, p1);
		Apertar(juiz);
		AfirmarJz("3. **A IDA**: o juiz atravessa pro Outro Mundo e chega na mesa do Enma, onde as almas chegam", Em(juiz, alem, mesa), Onde(juiz));
		AfirmarJz("...e a ficha guarda o lugar EXATO de onde ele saiu: as tres partes da zona e o ponto",
				  juiz.Ficha.VoltaDoJuizZona == terra.Name && juiz.Ficha.VoltaDoJuizTipo == terra.Kind && juiz.Ficha.VoltaDoJuizSeed == terra.Seed
				  && juiz.Ficha.VoltaDoJuizX == p1.X && juiz.Ficha.VoltaDoJuizY == p1.Y,
				  $"{juiz.Ficha.VoltaDoJuizZona} ({juiz.Ficha.VoltaDoJuizX},{juiz.Ficha.VoltaDoJuizY})");
		AfirmarJz("...e o fato da volta acende (`Fatos.VoltaDoOutroMundo`)", (FatosDe(juiz) & Fatos.VoltaDoOutroMundo) != 0);

		// ---------------------------------------------------------------- 4. o revive la dentro
		PorEm(juiz, alem, mesa);
		ServerPlayer MortoAoLado(int i, string nome)
		{
			ServerPlayer m = Forjar(i, nome, alem, Livre(alem, juiz.Pos + new Vec2(0.5f * T, 0)));
			m.Combate.Morrer(ignorarSeguro: true);
			m.MorteJaViajou = true;              // o morto que ja chegou ao Outro Mundo (de pe, de aureola)
			m.RelogioDaMorte = long.MaxValue;
			return m;
		}
		ServerPlayer alma1 = MortoAoLado(2, "AlmaUm");
		RevividoFicaNoAlemDeTeste = true;
		try { RessuscitarG4(juiz); }
		finally { RevividoFicaNoAlemDeTeste = false; }
		AfirmarJz("(defeito injetado: o revivido e puxado pra perto de quem o reviveu, como antes) a alma volta a vida e FICA VIVA no Outro "
				  + "Mundo, sem porta de saida -- a regua do 4 REPROVA", !alma1.Ficha.dead && Alem.EhOAlem(alma1.Zone), Onde(alma1));
		RemoverNpc(alma1);   // fora do caminho: o `Revive` pega o morto mais perto

		ServerPlayer alma2 = MortoAoLado(3, "AlmaDois");
		List<string> reviveu = Ouvir(() => RessuscitarG4(juiz));
		AfirmarJz("4. **O REVIVE DO JUIZ LA DENTRO**: a alma volta a vida e ACORDA NO MUNDO DOS VIVOS, em casa (o funil do renascer)",
				  !alma2.Ficha.dead && !Alem.EhOAlem(alma2.Zone) && alma2.Zone.Equals(DestinoDe(alma2).Zona), Onde(alma2));
		AfirmarJz("...e o juiz le que a alma desceu, e continua no Outro Mundo, vivo (a primeira volta de uma alma nao cobra nada)",
				  Disse(reviveu, "desce pro mundo dos vivos") && !juiz.Ficha.dead && Alem.EhOAlem(juiz.Zone), Ultimos(reviveu));

		// ---------------------------------------------------------------- 5. a volta
		PorEm(juiz, terra, p1);
		Apertar(juiz);
		VoltaDoJuizPeloBercoDeTeste = true;
		try { Apertar(juiz); }
		finally { VoltaDoJuizPeloBercoDeTeste = false; }
		AfirmarJz("(defeito injetado: a volta cai no berco, como toda saida paga do Outro Mundo) o juiz volta pros vivos LONGE de onde saiu "
				  + "-- a regua do 5 REPROVA", !Alem.EhOAlem(juiz.Zone) && !Em(juiz, terra, p1), Onde(juiz));

		PorEm(juiz, terra, p2);
		Apertar(juiz);
		bool foi = Em(juiz, alem, mesa);
		Apertar(juiz);
		AfirmarJz("5. **A VOLTA**: do Outro Mundo, o juiz volta pro mundo dos vivos no lugar EXATO de onde saiu", foi && Em(juiz, terra, p2), Onde(juiz));
		AfirmarJz("...e a volta e GASTA: a ficha esquece o lugar, e o fato apaga",
				  juiz.Ficha.VoltaDoJuizZona.Length == 0 && (FatosDe(juiz) & Fatos.VoltaDoOutroMundo) == 0);

		// ---------------------------------------------------------------- 6. o carimbo guarda a volta
		PorEm(juiz, terra, p3);
		juiz.Ficha.VoltaDoJuizZona = "";
		JulgarG8(juiz, "ceu");
		bool noCeu = string.Equals(juiz.Zone.Name, Alem.ZonaDoCeu, StringComparison.Ordinal);
		Apertar(juiz);
		AfirmarJz("6. O CARIMBO DO JUIZ (`Go_To_Heaven_Or_Hell`) leva ao Ceu E guarda a volta: a travessia apertada la o devolve ao ponto "
				  + "de onde ele bateu o carimbo", noCeu && Em(juiz, terra, p3), Onde(juiz));

		// ---------------------------------------------------------------- 7. o trono perdido la dentro
		PorEm(juiz, terra, p4);
		Apertar(juiz);
		_tronos.Remove(TravessiaDoJuiz.Cargo);
		try
		{
			bool veSemTrono = Ve(juiz);
			Apertar(juiz);
			AfirmarJz("7. O JUIZ QUE PERDEU O TRONO DO LADO DE LA continua vendo o botao (o fato da volta) e volta pro lugar exato",
					  veSemTrono && Em(juiz, terra, p4), Onde(juiz));

			PorEm(juiz, terra, p4);
			List<string> semTrono = ApertarOuvindo(juiz);
			AfirmarJz("...e no mundo dos vivos, sem trono, o botao some e a ida e recusada", !Ve(juiz) && Disse(semTrono, "só o King Yemma")
					  && Em(juiz, terra, p4), Ultimos(semTrono));
		}
		finally { _tronos[TravessiaDoJuiz.Cargo] = juiz.Conta; }

		// ---------------------------------------------------------------- 8. o lugar que sumiu
		PorEm(juiz, alem, mesa);
		juiz.Ficha.VoltaDoJuizZona = "Lugar_Que_Nao_Existe";
		juiz.Ficha.VoltaDoJuizTipo = ZoneKey.KindPremade;
		List<string> sumiu = ApertarOuvindo(juiz);
		AfirmarJz("8. O LUGAR QUE NAO EXISTE MAIS devolve o juiz ao mundo dos vivos em casa, com a frase",
				  juiz.Zone.Equals(DestinoDe(juiz).Zona) && Disse(sumiu, "não existe mais"), $"{Onde(juiz)} | {Ultimos(sumiu)}");

		// ---------------------------------------------------------------- 9. as portas
		PorEm(juiz, terra, p4);
		juiz.Combate.Nocautear(30);
		List<string> caido = ApertarOuvindo(juiz);
		AfirmarJz("9. AS PORTAS DE TODO SALTO: caido nao atravessa", Disse(caido, "caido") && Em(juiz, terra, p4), Ultimos(caido));

		// O FANTASMA DE PE NO OUTRO MUNDO (o juiz que morreu la): nao e cadaver -- quem esta caido no chao dos vivos ja ouviu o "caido"
		// acima --, e mesmo assim nao sai: o morto sai do Outro Mundo pagando, ou revivido.
		PorEm(juiz, alem, mesa);
		juiz.Combate.Morrer(ignorarSeguro: true);
		juiz.MorteJaViajou = true;
		juiz.RelogioDaMorte = long.MaxValue;
		List<string> morto = ApertarOuvindo(juiz);
		AfirmarJz("...e o FANTASMA de pe no Outro Mundo tambem nao: um morto nao sai de la por conta propria (sai pagando, ou revivido)",
				  Disse(morto, "um morto nao sai") && Em(juiz, alem, mesa), Ultimos(morto));

		PorEm(juiz, terra, p4);
		ZoneKey sala = ZoneKey.Premade(ZonaDaSala);
		if (_catalogo?.Get(sala) != null)
		{
			MoveToZone(juiz.Id, sala, PontoDeNascimento(sala));
			List<string> daSala = ApertarOuvindo(juiz);
			AfirmarJz("...e da Sala do Tempo nao se sai pela travessia (a volta seria pra dentro dela, e a porta dela cobra o que o "
					  + "teleporte pularia)", Disse(daSala, "daqui não") && juiz.Zone.Equals(sala), Ultimos(daSala));
			PorEm(juiz, terra, p4);
		}
		else AfirmarJz("...e da Sala do Tempo nao se sai pela travessia", false, $"a Sala ({ZonaDaSala}) nao tem mapa neste servidor");

		// ---------------------------------------------------------------- 10. o disco
		juiz.Ficha.VoltaDoJuizZona = "Namek";
		juiz.Ficha.VoltaDoJuizTipo = ZoneKey.KindPremade;
		juiz.Ficha.VoltaDoJuizSeed = 7;
		juiz.Ficha.VoltaDoJuizX = 1234.5f;
		juiz.Ficha.VoltaDoJuizY = 678.25f;
		Fighter lida = JsonSerializer.Deserialize<Fighter>(JsonSerializer.Serialize(juiz.Ficha, CopiaDeFicha), CopiaDeFicha)!;
		AfirmarJz("10. A VOLTA VIAJA COM A FICHA pro disco: o juiz que sai do jogo do lado de la volta pro mesmo lugar",
				  lida.VoltaDoJuizZona == "Namek" && lida.VoltaDoJuizTipo == ZoneKey.KindPremade && lida.VoltaDoJuizSeed == 7
				  && lida.VoltaDoJuizX == 1234.5f && lida.VoltaDoJuizY == 678.25f);
		juiz.Ficha.VoltaDoJuizZona = "";
	}
}
