using System.Text.Json;
using Godot;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>
/// O BATISMO DE PLANETAS -- quem pisou primeiro, quem domina, e o nome que cada mundo leva na tela.
///
/// O dono (2026-10-10): *"adicione tb a opçao de nomear o planeta (a primeira pessoa q entrou nele ou o
/// dono do planeta q dominou)"*. As regras e o porque de ser um nome de EXIBICAO (e nao um rename da zona,
/// como no DM) estao em `Core/World/NomesDePlanetas.cs`.
///
/// ============================ O QUE MORA AQUI ============================
///   * O LIVRO (`planetas-nomes.json`): um registro por planeta descoberto ou batizado.
///   * A DESCOBERTA: o primeiro JOGADOR a pisar num mundo GERADO, pelas duas portas de entrada no mundo
///     (o login e o `MoveToZone`) -- e nao so pelo pouso, como no original (`pspace_land`): quem chega de
///     nave grande, de teleporte ou renascendo tambem pisou.
///   * OS VERBOS (`planeta_nomear`, `planeta_desnomear`), que saem do cartao "Nome deste planeta" da aba World.
///   * O FIO (`S2C.NomesDePlanetas`): a lista dos batizados, igual pra todo mundo, mais o direito de quem
///     recebe sobre o chao em que esta -- e o que faz o cartao mostrar (ou nao) a caixa de texto.
///   * O NOME NA BOCA DO SERVIDOR (<see cref="NomeDoMundo"/>): o pouso, a morte e a volta de um planeta
///     falam o nome batizado.
/// ==========================================================================
/// </summary>
public partial class GameServer
{
	private readonly RegistroDeNomes _batismos = new();

	private string CaminhoDosBatismos => System.IO.Path.Combine(_store?.Pasta ?? ".", "planetas-nomes.json");

	/// <summary>
	/// OS NOMES QUE JA TEM DONO: os lugares do mapa (planetas e o alem) e o que o jogador ouve no lugar deles
	/// ("Terra"). Batizar um mundo de "Namek" poria dois Namek no chat. Montado uma vez, do manifesto.
	/// </summary>
	private HashSet<string>? _nomesConhecidos;

	private HashSet<string> NomesConhecidos()
	{
		if (_nomesConhecidos != null) return _nomesConhecidos;
		var nomes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Terra", Espaco.NomeDoEspaco };
		foreach (PlanetaNoEspaco p in Espaco.PreFeitos()) { nomes.Add(p.Nome); nomes.Add(p.Nome.Replace('_', ' ')); }
		if (_catalogo != null)
			foreach (ZoneEntry e in _catalogo.Todas) { nomes.Add(e.Zona); nomes.Add(e.Zona.Replace('_', ' ')); }
		return _nomesConhecidos = nomes;
	}

	// =====================================================================
	// CARGA E SALVAMENTO
	// =====================================================================
	private void CarregarBatismos()
	{
		try
		{
			if (!System.IO.File.Exists(CaminhoDosBatismos)) return;

			var lista = JsonSerializer.Deserialize<List<NomeDePlaneta>>(
				System.IO.File.ReadAllText(CaminhoDosBatismos),
				new JsonSerializerOptions { IncludeFields = true });
			if (lista == null) return;

			foreach (NomeDePlaneta n in lista)
			{
				if (n.Chave.Length == 0) { GD.PushWarning("[server] planetas-nomes.json: registro sem chave -- descartado"); continue; }
				_batismos.Por(n);
			}
			GD.Print($"[server] batismos: {_batismos.Batizados.Count()} planeta(s) com nome dado, "
				   + $"{_batismos.Quantos} no livro");
		}
		catch (Exception e) { GD.PushWarning($"[server] planetas-nomes.json ilegivel: {e.Message}"); }
	}

	private void SalvarBatismos()
	{
		// O PALCO DE BANCADA: o livro so muda na memoria. Ver `BatismosSoNaMemoriaDeTeste`.
		if (BatismosSoNaMemoriaDeTeste) return;
		if (_store == null) return;
		try
		{
			string tmp = CaminhoDosBatismos + ".tmp";
			System.IO.File.WriteAllText(tmp, JsonSerializer.Serialize(
				_batismos.Todos.ToList(),
				new JsonSerializerOptions { IncludeFields = true, WriteIndented = true }));
			System.IO.File.Move(tmp, CaminhoDosBatismos, overwrite: true);
		}
		catch (Exception e) { GD.PushWarning($"[server] nao consegui gravar planetas-nomes.json: {e.Message}"); }
	}

	// =====================================================================
	// O NOME NA BOCA DO SERVIDOR
	// =====================================================================
	/// <summary>
	/// COMO ESTE MUNDO SE CHAMA PRA QUEM LE: o nome batizado, ou o de sempre. Pra zona que nao e planeta (o
	/// espaco, o alem, o interior de uma nave), o proprio nome dela.
	///
	/// SEM BATISMO ELE DEVOLVE O NOME CRU DA ZONA, de proposito: as frases que passaram a usar isto (o pouso, a
	/// morte e a volta de um planeta) diziam `zona.Name`, e continuam dizendo letra por letra enquanto ninguem
	/// batizar. Quem fala "Terra" no lugar de "Earth" e o `NomeDoPlaneta`, que ja falava.
	/// </summary>
	private string NomeDoMundo(ZoneKey zona) =>
		ChaveDePlaneta.Da(zona) is { } chave ? _batismos.NomeVisto(chave.Texto, zona.Name) : zona.Name;

	/// <summary>O mesmo, a partir do corpo da carta estelar (o pouso so tem o `PlanetaNoEspaco` na mao).</summary>
	private string NomeDoMundo(PlanetaNoEspaco p) => _batismos.NomeVisto(ChaveDePlaneta.De(p).Texto, p.Nome);

	/// <summary>O nome que o BATISMO anuncia como "o de antes": o batizado, ou o que o jogador ouve ("Terra").</summary>
	private string NomeFaladoDe(ZoneKey zona, ChaveDePlaneta chave) =>
		_batismos.NomeVisto(chave.Texto, NomeDeCatalogo(zona.Name));

	// =====================================================================
	// A DESCOBERTA -- as duas portas de entrada no mundo chamam isto
	// =====================================================================
	/// <summary>
	/// ALGUEM ACABOU DE PISAR NUM CHAO (entrou no jogo, ou trocou de zona). Duas coisas dependem disso: o
	/// primeiro JOGADOR num mundo gerado vira o descobridor dele, e o direito de batizar de quem chegou muda
	/// com o chao -- por isso a lista de nomes vai de novo, com o direito recalculado.
	/// </summary>
	private void AoPisarNoMundo(ServerPlayer pl)
	{
		// E O CHAO TEM QUE EXISTIR NO SERVIDOR: quem entra num mundo sorteado sem ter pousado nele (o login
		// depois de um reinicio, a saida de uma nave) o acorda aqui. Ver `GameServer.MundoFrio.cs`.
		AcordarOMundoGerado(pl.Zone);

		AnotarDescoberta(pl);
		MandarBatismos(pl);
	}

	private void AnotarDescoberta(ServerPlayer pl)
	{
		// SO GENTE: um NPC nascendo, um clone ou o corpo largado de alguem nao descobrem nada.
		if (!EhJogador(pl) || pl.Assinatura.Length == 0) return;

		// SO MUNDO GERADO. Os planetas do mapa ja estavam la antes de qualquer personagem existir: o
		// "primeiro a pisar" na Terra seria quem logou primeiro depois de um wipe.
		if (ChaveDePlaneta.Da(pl.Zone) is not { PreFeito: false } chave) return;

		NomeDePlaneta n = _batismos.Abrir(chave.Texto, pl.Zone.Name);
		if (n.AssinaturaDoDescobridor.Length > 0) return;

		n.Descobridor = pl.Name;
		n.AssinaturaDoDescobridor = pl.Assinatura;
		SalvarBatismos();

		GD.Print($"[batismo] {pl.Name} e o primeiro a pisar em {pl.Zone.Name} ({chave.Texto})");
		Avisar(pl, "você é o primeiro a pisar neste mundo! Ele pode levar o nome que você der: "
				 + "menu P > World > \"Nome deste planeta\".");
	}

	// =====================================================================
	// OS VERBOS
	// =====================================================================
	private bool ComandoDeBatismo(ServerPlayer pl, string cmd, string arg)
	{
		switch (cmd)
		{
			case "planeta_nomear": BatizarPlaneta(pl, arg); return true;
			case "planeta_desnomear": TirarONomeDoPlaneta(pl); return true;
			default: return false;
		}
	}

	/// <summary>O direito DESTE jogador de batizar o chao em que ele esta, e o registro desse chao (nulo fora de planeta).</summary>
	private Batismo.Direito DireitoDeBatizarAqui(ServerPlayer pl, out NomeDePlaneta? registro)
	{
		registro = null;
		if (ChaveDePlaneta.Da(pl.Zone) is not { } chave) return Batismo.Direito.Nenhum;

		registro = _batismos.De(chave.Texto);
		Dominio? dominio = DominioDaZona(pl.Zone);
		bool donoAqui = dominio != null && pl.Assinatura.Length > 0
						&& string.Equals(dominio.Assinatura, pl.Assinatura, StringComparison.Ordinal);
		bool descobridor = registro != null && pl.Assinatura.Length > 0
						   && string.Equals(registro.AssinaturaDoDescobridor, pl.Assinatura, StringComparison.Ordinal);

		return Batismo.DireitoDe(EhAdmin(pl), donoAqui, temDono: dominio != null, descobridor,
								 direitoUsado: registro?.DireitoUsado ?? false);
	}

	private void BatizarPlaneta(ServerPlayer pl, string digitado)
	{
		if (ChaveDePlaneta.Da(pl.Zone) is not { } chave)
		{
			Avisar(pl, "só dá pra batizar um planeta estando nele.");
			return;
		}

		Batismo.Direito direito = DireitoDeBatizarAqui(pl, out NomeDePlaneta? registro);
		if (direito == Batismo.Direito.Nenhum)
		{
			Avisar(pl, PorQueNaoBatiza(pl, registro));
			return;
		}

		string nome = Batismo.Limpar(digitado);
		string antes = NomeFaladoDe(pl.Zone, chave);
		Batismo.Recusa recusa = Batismo.Validar(nome, antes, NomesConhecidos(), n => _batismos.EmUso(n, chave.Texto));
		if (recusa != Batismo.Recusa.Pode)
		{
			Avisar(pl, Batismo.Motivo(recusa));
			return;
		}

		// O INTERVALO E DO DONO: o descobridor so batiza uma vez, e o admin esta moderando.
		if (direito == Batismo.Direito.Dono && registro is { Nome.Length: > 0 }
			&& TempoDoMundo - registro.Quando < Batismo.SegundosEntreBatismos)
		{
			double faltam = Batismo.SegundosEntreBatismos - (TempoDoMundo - registro.Quando);
			Avisar(pl, $"este planeta acabou de ser batizado. Espere {Math.Ceiling(faltam / 60):0} minuto(s) pra trocar de novo.");
			return;
		}

		registro ??= _batismos.Abrir(chave.Texto, pl.Zone.Name);
		registro.Nome = nome;
		registro.PorQuem = pl.Name;
		registro.Quando = TempoDoMundo;
		if (direito == Batismo.Direito.Descobridor) registro.DireitoUsado = true;

		SalvarBatismos();
		MandarBatismosPraTodos();

		// A FRASE E A DO ORIGINAL (`pspace_rename_planet`, ProceduralSpace.dm:733).
		GD.Print($"[batismo] {pl.Name} ({direito}) batizou {pl.Zone.Name} ({chave.Texto}): '{antes}' -> '{nome}'");
		AnunciarNoMundo($"{pl.Name} batizou um mundo: {antes} agora se chama {nome}!");
	}

	private string PorQueNaoBatiza(ServerPlayer pl, NomeDePlaneta? registro)
	{
		Dominio? dominio = DominioDaZona(pl.Zone);
		if (dominio != null)
			return $"este planeta é domínio de {dominio.Nome}: só quem domina dá o nome.";
		if (registro is { AssinaturaDoDescobridor.Length: > 0 }
			&& string.Equals(registro.AssinaturaDoDescobridor, pl.Assinatura, StringComparison.Ordinal))
			return "você já usou o seu batismo neste mundo. Pra trocar o nome agora, só dominando o planeta.";
		return "só batiza um planeta quem pisou nele primeiro (em mundo gerado, e enquanto ele não tem dono) "
			 + "ou quem o domina.";
	}

	/// <summary>O planeta volta a se chamar o que sempre se chamou. So o dono e o admin: e desfazer um batismo.</summary>
	private void TirarONomeDoPlaneta(ServerPlayer pl)
	{
		Batismo.Direito direito = DireitoDeBatizarAqui(pl, out NomeDePlaneta? registro);
		if (direito is not (Batismo.Direito.Dono or Batismo.Direito.Admin))
		{
			Avisar(pl, "só quem domina o planeta tira o nome dele.");
			return;
		}
		if (registro is not { Nome.Length: > 0 })
		{
			Avisar(pl, "este planeta não tem nome dado.");
			return;
		}

		string antes = registro.Nome;
		registro.Nome = "";
		registro.PorQuem = "";
		registro.Quando = TempoDoMundo;
		SalvarBatismos();
		MandarBatismosPraTodos();

		GD.Print($"[batismo] {pl.Name} ({direito}) tirou o nome de {pl.Zone.Name}: '{antes}'");
		AnunciarNoMundo($"{antes} voltou a se chamar {NomeDeCatalogo(pl.Zone.Name)}.");
	}

	// =====================================================================
	// O FIO
	// =====================================================================
	/// <summary>
	/// A LISTA DOS BATIZADOS + o direito de QUEM RECEBE sobre o chao em que esta. Ver `S2C.NomesDePlanetas`.
	///
	/// A lista e igual pra todo mundo e so traz quem TEM nome dado (o registro de um mundo so descoberto nao
	/// interessa a ninguem alem do servidor). O cabecalho e pessoal: e ele que faz o cartao da aba World
	/// mostrar a caixa de texto pra quem pode usa-la, em vez de um botao que so sabe recusar.
	/// </summary>
	internal void MandarBatismos(ServerPlayer pl)
	{
		if (pl.Peer == null) return;

		Batismo.Direito direito = DireitoDeBatizarAqui(pl, out NomeDePlaneta? aqui);
		List<NomeDePlaneta> batizados = [.. _batismos.Batizados.Take(ushort.MaxValue)];

		var w = Protocol.Begin(Protocol.S2C.NomesDePlanetas);
		w.Put((byte)direito);
		w.Put(aqui?.Descobridor ?? "");
		w.Put((ushort)batizados.Count);
		foreach (NomeDePlaneta n in batizados)
		{
			w.Put(n.Chave);
			w.Put(n.Nome);
			w.Put(n.PorQuem);
		}
		EscutaDeBatismos?.Add((pl.Id, direito, batizados.Count));
		pl.Peer.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	private void MandarBatismosPraTodos()
	{
		foreach (ServerPlayer p in _players.Values) if (p.Peer != null) MandarBatismos(p);
	}
}
