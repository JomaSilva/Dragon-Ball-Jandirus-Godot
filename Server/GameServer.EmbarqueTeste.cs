using Godot;
using Jandirus.Core.Tech;

namespace Jandirus.Server;

/// <summary>
/// A METADE DE SERVIDOR DA BANCADA DO EMBARQUE (`--embarqueteste`) -- **e ela nao pontua nada**.
///
/// ============================ POR QUE ESTE ARQUIVO NAO TEM UM SO `Checa` ============================
/// A pergunta do dono e sobre um GESTO: *"ao apertar E perto delas... abrindo um menu dela, e ai vai
/// ter a opcao de entrar ou sair etc"*. Quem responde por um gesto e o cliente -- o menu que abriu, o
/// botao que estava nele, o dedo que o apertou. Uma checagem de servidor que afirmasse "o menu tem
/// Embarcar" estaria lendo a TABELA (`Interacoes.De`), e o arquivo `dbclimax-port-bancada-mede-
/// intencao` conta exatamente o que essa leitura vale: quatro defeitos visuais passaram por quatro
/// mil checagens verdes porque a bancada media a tabela e o defeito morava no widget.
///
/// Entao o placar inteiro mora no `RoboDeEmbarque`, e este arquivo e so o que o robo NAO consegue
/// fazer de dentro do jogo:
///
///   * dar a tecnologia e o zeni pra a Capital Ship caber (2.000.000z / tech 55) -- e o mesmo que
///     as outras bancadas de nave fazem, e pela mesma razao: um teste que exija farmar antes de
///     comecar nao e rodado;
///   * passar a nave pro nome de OUTRA PESSOA e tranca-la, que e a unica forma de exercitar as duas
///     recusas de dono e a de senha sem uma segunda conta ao vivo;
///   * derrubar o casco com alguem dentro, que e o caminho que nenhum verbo de jogador alcanca.
///
/// AS TRES SO EXISTEM COM A FLAG (ver `Verbo`), e as duas ultimas passam pelos MESMOS metodos de
/// producao que o mundo usa (`EstragarNave`, o campo `Senha` que o `EmbarcarNaNaveGrande` le). Uma
/// bancada que forjasse a recusa na mao mediria a propria mentira.
/// ================================================================================================
///
///     Godot --headless --path . --host --rede 7995 --embarqueteste --diagembarque
///           --conta bancemb --nome BancEmb
/// </summary>
public partial class GameServer
{
	private bool _embarqueDeTeste;

	/// <summary>
	/// A SENHA QUE A BANCADA POE NA NAVE ALHEIA. Seis algarismos DE PROPOSITO: o botao promete "um
	/// código de até 6 dígitos" (`Interacoes.De(Capital_Ship)`, `Forma.Numero, 0, 999999`) e o
	/// teclado do menu tinha teto de quatro -- o quinto dedo nao entrava e nada reclamava. Uma senha
	/// de quatro aqui deixaria essa porta fechada passar verde pra sempre.
	/// </summary>
	public const string SenhaDaBancada = "271828";

	/// <summary>A conta e o nome de quem a bancada finge ser o dono da nave.</summary>
	public const string ContaAlheiaDaBancada = "conta_de_fulano";
	public const string DonoAlheioDaBancada = "Fulano";

	/// <summary>
	/// PREPARA O CORPO PRA BANCADA -- tecnologia e zeni, e mais nada.
	///
	/// NAO ASSENTA A NAVE: quem a fabrica e a assenta e o robo, pela aba Tech e pelo verbo
	/// `posicionar`, que e o caminho do jogador. Uma nave posta aqui pularia o primeiro elo da
	/// corrente que esta sob teste.
	/// </summary>
	private void PrepararBancadaDeEmbarque(ServerPlayer pl)
	{
		pl.Ficha.techskill = 60;
		pl.Ficha.Zeni = 5_000_000;
		MandarFicha(pl);
		GD.Print($"[server] BANCADA DE EMBARQUE: {pl.Name} com tech 60 e 5.000.000z. "
				 + "O placar sai do lado do cliente (`--diagembarque`).");
	}

	/// <summary>
	/// OS TRES VERBOS DE FIXTURE. So respondem com a flag ligada -- sem ela nem chegam aqui.
	///
	/// Eles NAO sao interacao com objeto e nao entram no <see cref="Interacoes"/> de proposito: nada
	/// disto e coisa que um jogador possa fazer, e por-los no catalogo faria o menu da tecla E
	/// oferece-los a quem estivesse perto de uma nave.
	/// </summary>
	private bool ComandoDaBancadaDeEmbarque(ServerPlayer pl, string cmd, string arg)
	{
		switch (cmd)
		{
			// A NAVE PASSA PRO NOME DE OUTRA PESSOA, e trancada. E o unico jeito de uma bancada de
			// uma conta so exercitar "esta nave e de outro" -- e repare que ela mexe SO no dono e na
			// senha: as recusas continuam saindo dos metodos de producao, que leem esses dois campos.
			case "emb_alienar":
			{
				Nave? n = NavePerto(pl);
				if (n == null) { Avisar(pl, "[bancada] nao ha nave por perto pra alienar."); return true; }
				n.DonoConta = ContaAlheiaDaBancada;
				n.DonoNome = DonoAlheioDaBancada;
				n.Senha = SenhaDaBancada;
				GravarNaves();
				MandarObras(pl.Zone);
				Avisar(pl, $"[bancada] a nave #{n.Id} agora e de {DonoAlheioDaBancada} e esta trancada.");
				return true;
			}

			// E VOLTA A SER MINHA, destrancada. Sem isto a bancada teria que fabricar uma segunda
			// nave pra continuar, e o resto do percurso mediria uma nave que nunca foi de ninguem.
			case "emb_devolver":
			{
				Nave? n = NavePerto(pl) ?? NaveDesteInterior(pl);
				if (n == null) { Avisar(pl, "[bancada] nao ha nave por perto pra devolver."); return true; }
				n.DonoConta = pl.Conta;
				n.DonoNome = pl.Name;
				n.Senha = "";
				GravarNaves();
				MandarObras(pl.Zone);
				Avisar(pl, $"[bancada] a nave #{n.Id} voltou a ser sua, destrancada.");
				return true;
			}

			// O CASCO CEDE COM VOCE DENTRO. `autor` nulo e "nao ha algoz", que e o mesmo caminho do
			// corpo arremessado contra o casco -- e o unico que passa pela ejecao.
			case "emb_estragar":
			{
				Nave? n = NaveDesteInterior(pl) ?? NavePerto(pl);
				if (n == null) { Avisar(pl, "[bancada] nao ha nave pra estragar."); return true; }
				EstragarNave(n, n.ArmaduraMax * 5, null);
				return true;
			}

			default: return false;
		}
	}
}
