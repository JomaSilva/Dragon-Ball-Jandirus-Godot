using Godot;
using Jandirus.Core.Ranks;
using Jandirus.Core.Skills;
using Jandirus.Core.Stats;
using Jandirus.Core.Verbos;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--aprendizteste` (2026-10-19) -- O APRENDIZ DE KAIOSHIN (pedido do dono: *"conserte os 3"* -- os verbos do patronato eram
/// mudos). A regra mora em `Core/Ranks/AprendizDeKaioshin.cs`; o servidor, em `GameServer.AprendizDeKaioshin.cs`.
///
///     Godot --headless --server --port 9631 --aprendizteste
///
/// Dois clientes crus entram pela porta (o discipulo e o outro: o aprendiz e quem esta no JOGO) e o Kaioshin e forjado com o trono na
/// conta dele. Os verbos vao pelo funil de producao (`Verbo`), a compra pelo `Aprender`, o relogio pelo tique de 1 Hz dos cargos, e
/// cada familia tem o defeito injetado que a mesma regua REPROVA:
///   N. O KAIOSHIN TOMA UM APRENDIZ: o convite chega como cartao (o fato e o pedido) e o aceite amarra o vinculo.
///   A. A ARVORE ABRE, com a Cura e o Ver Mortos acesos nela (e sem vinculo, nao).
///   K. O KIT FICA: a Cura comprada nao some na reconciliacao do kit de cargo. `KitApagadoDeTeste`.
///   L. TRES POR KAIOSHIN. `SemLimiteDeTeste`.
///   Q. O VINCULO CAI COM O MESTRE FORA DO TRONO, e o kit comprado sai com os Marcos de volta. `VinculoEternoDeTeste`,
///      `SemDevolucaoDeTeste`.
///   F. A FORMATURA: o aprendiz que assume um cargo proprio esta formado.
///   O. QUEM ESTA FORA DO JOGO PERDE NA VOLTA (`ksap_login_check`).
///   X. DISPENSAR DEVOLVE OS MARCOS (a lista sobe pro cliente; a assinatura dispensa).
///   P/R/M/T. o titulo na aba People, a vaga reciclada que nao herda o mestre, o admin e o convite que vence.
/// </summary>
public partial class GameServer
{
	private const string ContaDoDiscipuloApz = "bancada_apz_discipulo";
	private const string ContaDoOutroApz = "bancada_apz_outro";
	private const string ContaDoMestreApz = "bancada_apz_mestre";
	private const int IdDoMestreApz = 99_800;
	private const string CuraApz = "/datum/skill/ki/Heal";

	private int _apzOk, _apzFalhou;

	private void AfirmarApz(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _apzOk++; GD.Print($"[aprendiz]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _apzFalhou++; GD.PrintErr($"[aprendiz]   FALHA {nome}   [{detalhe}]"); }
	}

	public void RodarBancadaDoAprendiz(int porta)
	{
		_apzOk = _apzFalhou = 0;
		GD.Print("[aprendiz] ================ O APRENDIZ DE KAIOSHIN (dono, 2026-10-19) ================");
		var tronosReais = new Dictionary<string, string>(_tronos, StringComparer.OrdinalIgnoreCase);
		var aprendizesReais = new Dictionary<string, VinculoDeAprendiz>(_aprendizes, StringComparer.Ordinal);
		_tronos.Clear();
		_aprendizes.Clear();
		var cliA = new ClienteCruDeTeste();
		var cliB = new ClienteCruDeTeste();
		var mesa = new MesaDeClientesCrus(this, porta, cliA, cliB);
		ServerPlayer? mestre = null;
		try
		{
			ServerPlayer? a = mesa.EntraNoMundo(cliA, ContaDoDiscipuloApz, "Discipulo");
			ServerPlayer? b = mesa.EntraNoMundo(cliB, ContaDoOutroApz, "Outro");
			AfirmarApz("(montagem) os dois clientes entram pela porta e sao jogadores", a != null && b != null && EhJogador(a) && EhJogador(b));
			if (a == null || b == null || _skills == null) return;
			mestre = ForjarOMestreApz(a);
			b.Pos = a.Pos + new Vec2(0, 2 * ZoneCollision.TileSize);
			if (!b.Zone.Equals(a.Zone)) MoveToZone(b.Id, a.Zone, b.Pos);

			ONomearApz(mestre, a, b);
			AArvoreEOKitApz(a, b);
			OLimiteApz(mestre, b);
			AQuedaApz(mestre, a);
			AFormaturaApz(mestre, a);
			AVoltaDeQuemSaiuApz(mesa, cliA, mestre);
			ODispensarApz(mesa, mestre);
			OsDetalhesApz(mesa, mestre);
		}
		catch (Exception e) { AfirmarApz("a bancada rodou ate o fim", false, e.ToString()); }
		finally
		{
			AprendizDeKaioshin.VinculoEternoDeTeste = false;
			AprendizDeKaioshin.SemLimiteDeTeste = false;
			AprendizDeKaioshin.KitApagadoDeTeste = false;
			AprendizDeKaioshin.SemDevolucaoDeTeste = false;
			if (mestre != null)
			{
				ZoneList(mestre.Zone.Hash).Remove(mestre);
				_players.Remove(mestre.Id);
			}
			mesa.Fecha();
			_ofertasDeAprendiz.Clear();
			_aprendizes.Clear();
			foreach ((string k, VinculoDeAprendiz v) in aprendizesReais) _aprendizes[k] = v;
			SalvarAprendizes();
			_tronos.Clear();
			foreach ((string k, string v) in tronosReais) _tronos[k] = v;
			SalvarCargos();
			if (_store != null)
				foreach (string conta in new[] { ContaDoDiscipuloApz, ContaDoOutroApz })
					try
					{
						string caminho = System.IO.Path.Combine(_store.Pasta, conta + ".json");
						if (System.IO.File.Exists(caminho)) System.IO.File.Delete(caminho);
					}
					catch (Exception e) { GD.PushWarning($"[aprendiz] nao apaguei '{conta}': {e.Message}"); }
			GD.Print($"[aprendiz] ================ {_apzOk} OK, {_apzFalhou} FALHA(S) ================");
		}
	}

	// ---- as maos ----

	/// <summary>O Kaioshin: um corpo forjado DE GENTE (conta e vaga), com o trono na conta, a 2 tiles do discipulo.</summary>
	private ServerPlayer ForjarOMestreApz(ServerPlayer perto)
	{
		var m = new ServerPlayer
		{
			Id = IdDoMestreApz, Peer = null, Name = "bancada: o Kaioshin", Race = "Kai", Genero = "Male", Idade = 300,
			Zone = perto.Zone, Pos = perto.Pos + new Vec2(2 * ZoneCollision.TileSize, 0), Conta = ContaDoMestreApz, Slot = 0,
			Ficha = new Fighter { Race = "Kai", BP = 1_000_000 }, Livro = new SkillBook(), Karma = 80,
		};
		m.Ficha.Class = "Normal";
		PorNoMundo(m);
		_tronos[AprendizDeKaioshin.CargoDoMestre] = m.Conta;
		return m;
	}

	private VinculoDeAprendiz? VinculoApz(ServerPlayer p) => _aprendizes.GetValueOrDefault(p.Assinatura);

	/// <summary>O convite pelo verbo de producao e o aceite: o vinculo de pe (sem marcos novos).</summary>
	private bool AmarrarApz(ServerPlayer mestre, ServerPlayer a)
	{
		_tronos[AprendizDeKaioshin.CargoDoMestre] = mestre.Conta;
		Verbo(mestre, AprendizDeKaioshin.VerboNomear, a.Id.ToString());
		Verbo(a, AprendizDeKaioshin.VerboAceitar, "");
		return EAprendizDeKaioshin(a);
	}

	/// <summary>Amarra e compra a Cura PELA arvore do aprendiz, pelo `Aprender` de producao. Devolve os marcos de antes da compra.</summary>
	private int AmarrarEComprarApz(ServerPlayer mestre, ServerPlayer a)
	{
		AmarrarApz(mestre, a);
		a.Livro.MarcosLivres += 3;
		int antes = a.Livro.MarcosLivres;
		Aprender(a, CuraApz);
		return antes;
	}

	private bool AvaliaACuraApz(ServerPlayer p) =>
		p.Livro.Avaliar(_skills!, CuraApz, RacaComSangue(p), p.Class, vilao: false).Motivo == Recusa.Pode;

	// =====================================================================
	// N) O CONVITE E O ACEITE
	// =====================================================================
	private void ONomearApz(ServerPlayer mestre, ServerPlayer a, ServerPlayer b)
	{
		GD.Print("[aprendiz] -- N) o convite e o aceite --");
		List<string> ofereceu = Ouvir(() => Verbo(mestre, AprendizDeKaioshin.VerboNomear, a.Id.ToString()));
		bool fato = FatosDe(a).HasFlag(Fatos.OfertaDeAprendiz) && !FatosDe(b).HasFlag(Fatos.OfertaDeAprendiz);
		var pedidos = new List<PedidoNaTela>();
		PedidosDe(a, pedidos);
		bool cartao = pedidos.Any(p => p.Fato == Fatos.OfertaDeAprendiz && p.Texto.Contains("Aprendiz de Kaioshin", StringComparison.Ordinal));
		bool bitAlto = (ulong)Fatos.OfertaDeAprendiz > uint.MaxValue;
		List<string> aceitou = Ouvir(() => Verbo(a, AprendizDeKaioshin.VerboAceitar, ""));
		AfirmarApz("N. **O KAIOSHIN TOMA UM APRENDIZ**: o Nomear com o discipulo marcado vira o fato e o cartao de pedido dele (o 33o bit, "
				   + "acima do `uint`), e o Aceitar amarra o vinculo",
				   fato && cartao && bitAlto && EAprendizDeKaioshin(a) && Disse(aceitou, "agora um Aprendiz de Kaioshin"),
				   $"fato {fato}; cartao {cartao}; bit alto {bitAlto}; vinculo {EAprendizDeKaioshin(a)} | {Ultimos(ofereceu)} || {Ultimos(aceitou)}");
		AfirmarApz("N. quem nao e Kaioshin aperta o Nomear e ouve a recusa (o requisito do botao)",
				   Disse(Ouvir(() => Verbo(b, AprendizDeKaioshin.VerboNomear, a.Id.ToString())), "só o Kaioshin no trono"));
		AfirmarApz("P. a aba People mostra \"Aprendiz de Kaioshin\" no lugar do cargo de quem tem o vinculo (e nada de quem nao tem)",
				   TituloSemCargo(a) == AprendizDeKaioshin.Titulo && TituloSemCargo(b).Length == 0, $"{TituloSemCargo(a)} / {TituloSemCargo(b)}");

		// (2026-10-20, a r1) A ARVORE CHEGA COM UMA FRASE: a do vinculo. A do treino ("o que voce treinou abriu um caminho novo") mentia --
		// e a foto `aprendiz-2-arvore` da r1 mostrava as duas.
		AfirmarApz("N. a arvore do aprendiz chega com UMA frase -- a do vinculo, e nao a do treino",
				   Disse(aceitou, "se abriu na aba Learning") && !Disse(aceitou, "o que voce treinou abriu"), Ultimos(aceitou));
		_aprendizes.Remove(a.Assinatura);
		AplicarEfeitos(a);   // a arvore fecha: o aceite de baixo a abre de novo
		AprendizDeKaioshin.TreinoAnunciaDeTeste = true;
		List<string> comDefeito;
		try
		{
			comDefeito = Ouvir(() =>
			{
				Verbo(mestre, AprendizDeKaioshin.VerboNomear, a.Id.ToString());
				Verbo(a, AprendizDeKaioshin.VerboAceitar, "");
			});
		}
		finally { AprendizDeKaioshin.TreinoAnunciaDeTeste = false; }
		AfirmarApz("(defeito injetado: a frase do treino no aceite, o jogo da r1) a mesma regua REPROVA -- duas frases pra mesma arvore",
				   EAprendizDeKaioshin(a) && Disse(comDefeito, "se abriu na aba Learning") && Disse(comDefeito, "o que voce treinou abriu"),
				   Ultimos(comDefeito));
	}

	// =====================================================================
	// A/K) A ARVORE E O KIT
	// =====================================================================
	private void AArvoreEOKitApz(ServerPlayer a, ServerPlayer b)
	{
		GD.Print("[aprendiz] -- A/K) a arvore e o kit --");
		AplicarEfeitos(a);
		AplicarEfeitos(b);
		bool aberta = a.Livro.Arvores.Any(e => string.Equals(e.Path, AprendizDeKaioshin.Arvore, StringComparison.OrdinalIgnoreCase));
		a.Livro.MarcosLivres += 3;
		b.Livro.MarcosLivres += 3;
		AfirmarApz("A. **A ARVORE ABRE**: com o vinculo de pe, a arvore 'Aprendiz de Kaioshin' e do discipulo e a Cura (skill de cargo, "
				   + "apagada no catalogo) se compra nela -- e sem vinculo, nao",
				   aberta && AvaliaACuraApz(a) && !AvaliaACuraApz(b) && !b.Livro.Arvores.Any(e => e.Path == AprendizDeKaioshin.Arvore),
				   $"aberta {aberta}; cura do discipulo {AvaliaACuraApz(a)}; cura do outro {AvaliaACuraApz(b)}");

		int antes = a.Livro.MarcosLivres;
		Aprender(a, CuraApz);
		bool comprou = a.Livro.Sabe(CuraApz) && a.Livro.MarcosLivres == antes - 1;
		TickDosCargos();
		AfirmarApz("K. **O KIT FICA**: a Cura comprada na arvore do aprendiz passa pelo tique dos cargos e continua no livro (e anotada como "
				   + "compra do aprendizado)",
				   comprou && a.Livro.Sabe(CuraApz) && VinculoApz(a)?.Compradas.Contains(CuraApz) == true,
				   $"comprou {comprou}; sabe {a.Livro.Sabe(CuraApz)}; anotada {VinculoApz(a)?.Compradas.Contains(CuraApz)}");
		AprendizDeKaioshin.KitApagadoDeTeste = true;
		try { TickDosCargos(); }
		finally { AprendizDeKaioshin.KitApagadoDeTeste = false; }
		AfirmarApz("(defeito injetado: a reconciliacao apaga o kit, o jogo de antes) a mesma regua REPROVA -- a Cura some do aprendiz",
				   !a.Livro.Sabe(CuraApz));
		a.Livro.MarcosLivres += 1;
		Aprender(a, CuraApz);
		TickDosCargos();
	}

	// =====================================================================
	// L) TRES POR KAIOSHIN
	// =====================================================================
	private void OLimiteApz(ServerPlayer mestre, ServerPlayer b)
	{
		GD.Print("[aprendiz] -- L) tres por Kaioshin --");

		bool Quarto(out string detalhe)
		{
			_ofertasDeAprendiz.Remove(b.Conta);
			_aprendizes["apz_fantasma_1"] = new VinculoDeAprendiz { ContaDoMestre = mestre.Conta, NomeDoAprendiz = "fantasma 1" };
			_aprendizes["apz_fantasma_2"] = new VinculoDeAprendiz { ContaDoMestre = mestre.Conta, NomeDoAprendiz = "fantasma 2" };
			List<string> falas = Ouvir(() => Verbo(mestre, AprendizDeKaioshin.VerboNomear, b.Id.ToString()));
			_aprendizes.Remove("apz_fantasma_1");
			_aprendizes.Remove("apz_fantasma_2");
			detalhe = $"guiava {QuantosGuia(mestre.Conta) + 2}; oferta {_ofertasDeAprendiz.ContainsKey(b.Conta)} | {Ultimos(falas)}";
			bool recusou = !_ofertasDeAprendiz.ContainsKey(b.Conta) && Disse(falas, "já guia 3 aprendizes");
			_ofertasDeAprendiz.Remove(b.Conta);
			return recusou;
		}

		bool certo = Quarto(out string d1);
		AfirmarApz("L. **TRES POR KAIOSHIN**: com tres aprendizes, o Nomear recusa o quarto", certo, d1);
		AprendizDeKaioshin.SemLimiteDeTeste = true;
		bool comDefeito;
		string d2;
		try { comDefeito = Quarto(out d2); }
		finally { AprendizDeKaioshin.SemLimiteDeTeste = false; }
		AfirmarApz("(defeito injetado: sem limite) a mesma regua REPROVA -- o quarto discipulo recebe o convite", !comDefeito, d2);
	}

	// =====================================================================
	// Q) A QUEDA COM O MESTRE FORA DO TRONO
	// =====================================================================
	private void AQuedaApz(ServerPlayer mestre, ServerPlayer a)
	{
		GD.Print("[aprendiz] -- Q) a queda com o mestre fora do trono --");

		bool Queda(out string detalhe)
		{
			_aprendizes.Remove(a.Assinatura);
			if (a.Livro.Sabe(CuraApz)) a.Livro.EsquecerEReembolsar(_skills!, CuraApz, RacaComSangue(a), a.Class);
			int antes = AmarrarEComprarApz(mestre, a);
			_tronos.Remove(AprendizDeKaioshin.CargoDoMestre);
			List<string> falas = Ouvir(TickDosCargos);
			detalhe = $"vinculo {_aprendizes.ContainsKey(a.Assinatura)}; cura {a.Livro.Sabe(CuraApz)}; marcos {a.Livro.MarcosLivres} de {antes} | {Ultimos(falas)}";
			return !_aprendizes.ContainsKey(a.Assinatura) && !a.Livro.Sabe(CuraApz) && a.Livro.MarcosLivres == antes
				   && Disse(falas, "chegou ao fim") && Disse(falas, "trono do Kaioshin está vago");
		}

		AprendizDeKaioshin.VinculoEternoDeTeste = true;
		bool eterno;
		string d0;
		try { eterno = Queda(out d0); }
		finally { AprendizDeKaioshin.VinculoEternoDeTeste = false; }
		AfirmarApz("(defeito injetado: o vinculo eterno) a mesma regua REPROVA -- o mestre sai do trono e o aprendiz continua", !eterno, d0);

		bool certo = Queda(out string d1);
		AfirmarApz("Q. **O VINCULO CAI COM O MESTRE FORA DO TRONO**: vago o trono do Kaioshin, o tique desfaz o aprendizado -- a Cura comprada "
				   + "sai e os Marcos voltam", certo, d1);

		AprendizDeKaioshin.SemDevolucaoDeTeste = true;
		bool semDevolucao;
		string d2;
		try { semDevolucao = Queda(out d2); }
		finally { AprendizDeKaioshin.SemDevolucaoDeTeste = false; }
		AfirmarApz("(defeito injetado: a queda sem devolucao) a mesma regua REPROVA -- o aprendiz perde o investimento", !semDevolucao, d2);
		_tronos[AprendizDeKaioshin.CargoDoMestre] = mestre.Conta;
		_aprendizes.Remove(a.Assinatura);
		TickDosCargos();
	}

	// =====================================================================
	// F) A FORMATURA
	// =====================================================================
	private void AFormaturaApz(ServerPlayer mestre, ServerPlayer a)
	{
		GD.Print("[aprendiz] -- F) a formatura --");
		if (a.Livro.Sabe(CuraApz)) a.Livro.EsquecerEReembolsar(_skills!, CuraApz, RacaComSangue(a), a.Class);
		int antes = AmarrarEComprarApz(mestre, a);
		_tronos["turtle"] = a.Conta;
		List<string> falas = Ouvir(TickDosCargos);
		AfirmarApz("F. **A FORMATURA**: o aprendiz que assume um cargo proprio esta formado -- o vinculo cai e os Marcos voltam",
				   !_aprendizes.ContainsKey(a.Assinatura) && a.Livro.MarcosLivres == antes && Disse(falas, "está formado"),
				   $"vinculo {_aprendizes.ContainsKey(a.Assinatura)}; marcos {a.Livro.MarcosLivres} de {antes} | {Ultimos(falas)}");
		_tronos.Remove("turtle");
		TickDosCargos();
	}

	// =====================================================================
	// O) QUEM ESTA FORA DO JOGO
	// =====================================================================
	private void AVoltaDeQuemSaiuApz(MesaDeClientesCrus mesa, ClienteCruDeTeste cliA, ServerPlayer mestre)
	{
		GD.Print("[aprendiz] -- O) quem esta fora do jogo perde na volta --");
		ServerPlayer? a = mesa.CorpoDe(ContaDoDiscipuloApz);
		if (a == null) { AfirmarApz("O. (montagem) o discipulo ainda esta no jogo", false); return; }
		if (a.Livro.Sabe(CuraApz)) a.Livro.EsquecerEReembolsar(_skills!, CuraApz, RacaComSangue(a), a.Class);
		int antes = AmarrarEComprarApz(mestre, a);
		string sig = a.Assinatura;
		bool saiu = mesa.SaiDoJogo(cliA, ContaDoDiscipuloApz);
		_tronos.Remove(AprendizDeKaioshin.CargoDoMestre);
		TickDosCargos();
		bool esperando = _aprendizes.TryGetValue(sig, out VinculoDeAprendiz? v) && v.Caiu.Length > 0;
		ServerPlayer? volta = mesa.EntraNoMundo(cliA, ContaDoDiscipuloApz, "Discipulo");
		List<string> falas = volta == null ? [] : Ouvir(TickDosCargos);
		AfirmarApz("O. **QUEM ESTA FORA DO JOGO PERDE NA VOLTA**: o vinculo caido do aprendiz offline espera; quando ele entra, o tique desfaz "
				   + "o aprendizado e os Marcos voltam",
				   saiu && esperando && volta != null && !_aprendizes.ContainsKey(sig) && !volta.Livro.Sabe(CuraApz)
				   && volta.Livro.MarcosLivres == antes && Disse(falas, "chegou ao fim"),
				   $"saiu {saiu}; esperando {esperando}; voltou {volta != null}; marcos {volta?.Livro.MarcosLivres} de {antes} | {Ultimos(falas)}");
		_tronos[AprendizDeKaioshin.CargoDoMestre] = mestre.Conta;
		if (volta != null) volta.Pos = mestre.Pos + new Vec2(-2 * ZoneCollision.TileSize, 0);
	}

	// =====================================================================
	// X) DISPENSAR
	// =====================================================================
	private void ODispensarApz(MesaDeClientesCrus mesa, ServerPlayer mestre)
	{
		GD.Print("[aprendiz] -- X) dispensar --");
		ServerPlayer? a = mesa.CorpoDe(ContaDoDiscipuloApz);
		if (a == null) { AfirmarApz("X. (montagem) o discipulo esta no jogo", false); return; }
		if (!a.Zone.Equals(mestre.Zone)) MoveToZone(a.Id, mestre.Zone, mestre.Pos + new Vec2(-2 * ZoneCollision.TileSize, 0));
		if (a.Livro.Sabe(CuraApz)) a.Livro.EsquecerEReembolsar(_skills!, CuraApz, RacaComSangue(a), a.Class);
		int antes = AmarrarEComprarApz(mestre, a);
		EscutaDeEscolhas = [];
		List<string> lista;
		try
		{
			Verbo(mestre, AprendizDeKaioshin.VerboDispensar, "");
			lista = EscutaDeEscolhas.LastOrDefault(e => e.Quem == mestre.Id && e.Verbo == AprendizDeKaioshin.VerboDispensar).Itens ?? [];
		}
		finally { EscutaDeEscolhas = null; }
		List<string> falas = Ouvir(() => Verbo(mestre, AprendizDeKaioshin.VerboDispensar, a.Assinatura));
		AfirmarApz("X. **DISPENSAR DEVOLVE OS MARCOS**: o Dismiss sem nome manda a lista dos aprendizes pro cliente e, com a assinatura, "
				   + "dispensa -- a Cura comprada sai e os Marcos voltam",
				   lista.Contains($"{a.Assinatura}|{a.Name}") && !_aprendizes.ContainsKey(a.Assinatura) && !a.Livro.Sabe(CuraApz)
				   && a.Livro.MarcosLivres == antes && Disse(falas, "você dispensou"),
				   $"lista [{string.Join(", ", lista)}]; vinculo {_aprendizes.ContainsKey(a.Assinatura)}; marcos {a.Livro.MarcosLivres} de {antes} "
				   + $"| {Ultimos(falas)}");
	}

	// =====================================================================
	// R/M/T) A VAGA RECICLADA, O ADMIN, O CONVITE QUE VENCE
	// =====================================================================
	private void OsDetalhesApz(MesaDeClientesCrus mesa, ServerPlayer mestre)
	{
		GD.Print("[aprendiz] -- R/M/T) a vaga reciclada, o admin e o convite que vence --");
		ServerPlayer? a = mesa.CorpoDe(ContaDoDiscipuloApz);
		if (a == null) { AfirmarApz("R. (montagem) o discipulo esta no jogo", false); return; }

		AmarrarApz(mestre, a);
		EsquecerAAssinaturaNoMundo(a.Assinatura);
		AfirmarApz("R. **A VAGA RECICLADA NAO HERDA O MESTRE**: o personagem que sai da vaga leva o vinculo junto",
				   !_aprendizes.ContainsKey(a.Assinatura));

		AdminOutorgarCargo(mestre, $"{a.Id}|aprendiz");
		bool deu = EAprendizDeKaioshin(a);
		AdminDestituirCargo(mestre, $"{a.Id}|Kaioshin Apprentice");
		AfirmarApz("M. o admin da (\"aprendiz\") e tira (\"Kaioshin Apprentice\") o aprendizado pelo painel de cargo",
				   deu && !_aprendizes.ContainsKey(a.Assinatura), $"deu {deu}; tirou {!_aprendizes.ContainsKey(a.Assinatura)}");

		Verbo(mestre, AprendizDeKaioshin.VerboNomear, a.Id.ToString());
		if (_ofertasDeAprendiz.TryGetValue(a.Conta, out (string ContaDoMestre, string NomeDoMestre, long Ate) o))
			_ofertasDeAprendiz[a.Conta] = (o.ContaDoMestre, o.NomeDoMestre, NowMs() - 1);
		TickDosCargos();
		List<string> tarde = Ouvir(() => Verbo(a, AprendizDeKaioshin.VerboAceitar, ""));
		AfirmarApz("T. o convite vence em 60 s: o tique o tira da mesa, e o aceite atrasado ouve que ninguem ofereceu",
				   !_ofertasDeAprendiz.ContainsKey(a.Conta) && !EAprendizDeKaioshin(a) && Disse(tarde, "ninguém te ofereceu"), Ultimos(tarde));
	}
}
