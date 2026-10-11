using Godot;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// RESTAURAR PLANETA PELA LISTA -- a familia 16 da `--planetateste` e o palco da `--diagadmin`.
///
/// O dono (2026-10-10): *"adicione um botao pros adms poderem restaurarem planetas q foram destruidos
/// (atualmente so da pra restaurar planetas q vc esta, caso eu esteja em outro planeta e queira reconstruir
/// a terra n é possivel, queria q fosse possivel eu ver a lista de planetas destruidos e selecionar os q
/// quero restaurar)"*.
///
/// ============================ DUAS METADES, DUAS BANCADAS ============================
///   * A REGRA mora aqui, dentro da `--planetateste` (que ja fotografa e devolve o livro dos mortos): o
///     verb com a CHAVE restaura o planeta certo de longe, recusa o que ainda esta morrendo e o que nao
///     esta na lista, e nao mexe na forma antiga (sem argumento).
///   * O FIO E A TELA moram na `--diagadmin` (`Client/RoboDeAdmin.cs`): o planeta chega pelo `S2C.Mortos`,
///     vira linha do cartao "Planetas destruidos", e o clique no botao restaura no servidor. Os tres
///     ganchos do fim deste arquivo sao o palco dela.
///
/// As duas leem o MESMO defeito injetado (<see cref="RestauraSoOndeEstaDeTeste"/>): a regua da regra e a
/// regua do clique tem de reprovar com ele.
/// ====================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// Um mundo GERADO DE MENTIRA. A seed e escolhida a mao (a enumeracao do universo deriva as dela por
	/// hash), entao a chave `#seed` dele nao e a de nenhum planeta que exista: por-lo na lista de mortos e
	/// tira-lo de la nao encosta em nada do mundo de quem roda a bancada.
	/// </summary>
	private static ZoneKey ZonaDoPlanetaDeMentira => ZoneKey.Procedural("Mundo de Bancada", 0x6199_E7UL);

	// =====================================================================
	// 16. O ADMIN RESTAURA PELA LISTA, DE OUTRO PLANETA
	// =====================================================================
	private void MedirARestauracaoPelaLista(Checagem Checa, ServerPlayer pl)
	{
		_mortos.Limpar();

		ZoneKey terra = ZoneKey.Premade("Earth");
		ZoneKey arlia = Cobaia;
		ZoneKey vegeta = ZoneKey.Premade("Vegeta");
		ZoneKey gerado = ZonaDoPlanetaDeMentira;

		// O ADMIN FICA NA TERRA, VIVA: e o "estou em outro planeta" do pedido.
		ZoneKey voltaZona = pl.Zone;
		Vec2 voltaPos = pl.Pos;
		if (!pl.Zone.Equals(terra)) MoveToZone(pl.Id, terra, PontoDeNascimento(terra));

		// O PLANETA DESTRUIDO ENTRA DIRETO NO REGISTRO, sem `ComecarDestruicao`: e o estado de um servidor
		// que acabou de subir com o livro dos mortos lido do disco -- que e justamente quando o admin abre
		// a aba e quer restaurar. (O caminho que MATA e o assunto das outras familias desta bancada.)
		static string ChaveDe(ZoneKey z) => ChaveDePlaneta.Da(z)!.Value.Texto;
		void Destruir(ZoneKey z) =>
			_mortos.Por(new EstadoDaMorte { Chave = ChaveDe(z), Nome = z.Name, Fase = FaseDaMorte.Destruido });
		string Fases() =>
			$"arlia={MorteDaZona(arlia)?.Fase} gerado={MorteDaZona(gerado)?.Fase} vegeta={MorteDaZona(vegeta)?.Fase} admin em {pl.Zone.Name}";

		bool defeitoAntes = RestauraSoOndeEstaDeTeste;
		try
		{
			Destruir(arlia);
			Destruir(gerado);
			_mortos.Por(new EstadoDaMorte
			{
				Chave = ChaveDe(vegeta), Nome = vegeta.Name, Fase = FaseDaMorte.Morrendo, Estagio = 1, Faltam = 100,
			});

			Checa("(montagem) o admin esta na Terra, VIVA; Arlia e um mundo gerado estao destruidos; Vegeta esta morrendo",
				  pl.Zone.Equals(terra) && !ZonaCondenada(terra) && ZonaMorta(arlia) && ZonaMorta(gerado)
				  && ZonaCondenada(vegeta) && !ZonaMorta(vegeta), Fases());

			// ---- A CONTRAPROVA PRIMEIRO: o verb de antes de 2026-10-10 ----
			List<string> comDefeito;
			RestauraSoOndeEstaDeTeste = true;
			try { comDefeito = Ouvir(() => VerboDeAdmin(pl, "admin_restaurar_planeta", ChaveDe(arlia))); }
			finally { RestauraSoOndeEstaDeTeste = false; }
			Checa("(defeito injetado: o verb so olha onde o admin esta) pedir Arlia DA TERRA nao restaura Arlia -- e a resposta fala da Terra",
				  ZonaMorta(arlia) && Disse(comDefeito, "Earth nao esta na lista de mortos"),
				  $"{Fases()} | {Ultimos(comDefeito)}");

			// ---- PELA LISTA, DE OUTRO PLANETA ----
			// Pelo `VerboDeAdmin`, e nao chamando o metodo: e o `switch` quem tem de ENTREGAR o argumento.
			List<string> falas = Ouvir(() => VerboDeAdmin(pl, "admin_restaurar_planeta", ChaveDe(arlia)));
			Checa("**O ADMIN RESTAURA PELA LISTA, DE OUTRO PLANETA**: da Terra, com a chave de Arlia, Arlia volta a existir",
				  !ZonaCondenada(arlia) && Disse(falas, "Arlia volta a existir"), $"{Fases()} | {Ultimos(falas)}");
			Checa("...e SO ela: o gerado continua destruido, Vegeta continua morrendo, e o admin nao saiu da Terra",
				  ZonaMorta(gerado) && ZonaCondenada(vegeta) && !ZonaMorta(vegeta) && pl.Zone.Equals(terra), Fases());

			// ---- O GERADO ----
			falas = Ouvir(() => VerboDeAdmin(pl, "admin_restaurar_planeta", ChaveDe(gerado)));
			Checa("um mundo GERADO sai pela chave dele, que e a SEED (o nome nao enderecaria um gerado)",
				  ChaveDe(gerado).StartsWith('#') && !ZonaCondenada(gerado)
				  && Disse(falas, $"{gerado.Name} volta a existir"), $"{Fases()} | {Ultimos(falas)}");

			// ---- O QUE AINDA ESTA MORRENDO ----
			falas = Ouvir(() => VerboDeAdmin(pl, "admin_restaurar_planeta", ChaveDe(vegeta)));
			Checa("**QUEM AINDA ESTA MORRENDO NAO SAI POR AQUI** (isso e o Abort Planet Death, que tambem apaga o ceu da explosao): Vegeta continua condenado, e a resposta aponta a outra porta",
				  MorteDaZona(vegeta)?.Fase == FaseDaMorte.Morrendo && Disse(falas, "Abort Planet Death"),
				  $"{Fases()} | {Ultimos(falas)}");

			// ---- O QUE NAO ESTA NA LISTA ----
			int antes = _mortos.Quantos;
			falas = Ouvir(() =>
			{
				VerboDeAdmin(pl, "admin_restaurar_planeta", "Planeta Que Nao Existe");
				VerboDeAdmin(pl, "admin_restaurar_planeta", "#42");
				VerboDeAdmin(pl, "admin_restaurar_planeta", ChaveDe(arlia));   // ja restaurado ali em cima
			});
			Checa("chave que nao esta na lista (inventada, ou de um planeta que ja voltou) nao muda nada, e as tres sao respondidas",
				  _mortos.Quantos == antes
				  && falas.Count(f => f.Contains("nao esta na lista de mortos", StringComparison.OrdinalIgnoreCase)) == 3,
				  $"{_mortos.Quantos} na lista (eram {antes}) | {Ultimos(falas)}");

			// ---- A FORMA ANTIGA CONTINUA DE PE ----
			falas = Ouvir(() => VerboDeAdmin(pl, "admin_restaurar_planeta", ""));
			Checa("SEM ARGUMENTO o verb continua olhando onde o admin esta: na Terra viva, ele responde que a Terra nao esta morta",
				  Disse(falas, "Earth nao esta na lista de mortos") && _mortos.Quantos == antes, Ultimos(falas));

			MoveToZone(pl.Id, arlia, PontoDeNascimento(arlia));
			Destruir(arlia);
			falas = Ouvir(() => VerboDeAdmin(pl, "admin_restaurar_planeta", ""));
			Checa("...e EM CIMA do planeta morto ele o restaura, como sempre fez",
				  pl.Zone.Equals(arlia) && !ZonaCondenada(arlia) && Disse(falas, "Arlia volta a existir"),
				  $"{Fases()} | {Ultimos(falas)}");
		}
		finally
		{
			RestauraSoOndeEstaDeTeste = defeitoAntes;
			_mortos.Limpar();
			if (!pl.Zone.Equals(voltaZona)) MoveToZone(pl.Id, voltaZona, voltaPos); else pl.Pos = voltaPos;
		}
	}

	// =====================================================================
	// O PALCO DA `--diagadmin`
	// =====================================================================
	private PalcoDeMortes? _palcoDaListaDeAdmin;

	/// <summary>
	/// PALCO DA `--diagadmin`: destroi o mundo de mentira e avisa os clientes pela porta de producao
	/// (`MandarMortosPraTodos`). Devolve a CHAVE dele -- o que o botao do cartao tem de mandar de volta.
	///
	/// DENTRO DO `PalcoDeMortes`: enquanto ele esta aberto nenhuma gravacao do `planetas-mortos.json`
	/// acontece, e ao fechar o registro volta a ser o que era. A bancada pode rodar contra a pasta de
	/// verdade sem deixar rastro.
	/// </summary>
	internal string DestruirPlanetaDeMentira()
	{
		_palcoDaListaDeAdmin ??= PalcoDeMortesDeBancada();
		ZoneKey z = ZonaDoPlanetaDeMentira;
		string chave = ChaveDePlaneta.Da(z)!.Value.Texto;
		_mortos.Por(new EstadoDaMorte { Chave = chave, Nome = z.Name, Fase = FaseDaMorte.Destruido });
		MandarMortosPraTodos();
		return chave;
	}

	/// <summary>O mundo de mentira ainda esta na lista de mortos? E a pergunta que a `--diagadmin` faz ao servidor.</summary>
	internal bool PlanetaDeMentiraEstaMorto() => ZonaMorta(ZonaDoPlanetaDeMentira);

	/// <summary>Fecha o palco aberto pelo <see cref="DestruirPlanetaDeMentira"/>. Idempotente.</summary>
	internal void FecharOPalcoDaListaDeAdmin()
	{
		_palcoDaListaDeAdmin?.Dispose();
		_palcoDaListaDeAdmin = null;
	}
}
