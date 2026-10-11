using Jandirus.Core.Economia;

namespace Jandirus.Core.World;

/// <summary>
/// O PRECO DE CADA PECA DE BASE, e com o que se paga.
///
/// ============================ O PEDIDO ============================
/// O dono (2026-10-10): *"construcao de base: construir paredes etc tem custo em zenni/ recursos o usuario
/// escolhe na hora de construir e essa opcao fica selecionada ate ele quiser trocar (e uma opcao no canto
/// inferior direito)"*.
/// ==================================================================
///
/// ============================ O QUE O ORIGINAL TEM ============================
/// Nada cobrado. `obj/var/buildcost` e `turf/build/var/buildcost` (`buildable.dm:352`, `:368`) sao declarados e
/// nunca lidos nem escritos: o `BuildATileHere` (`:379-461`) ergue de graca. O unico preco POR TILE que o DM
/// escreve e o do Recreator, que re-ergue um pedaco de base salvo -- 10 zeni por turf e 100 por objeto
/// (`buildable.dm:271`, `:276`, cobrado em `:321-324`).
/// ==============================================================================
///
/// ============================ O QUE VIROU (decisao deste port, anotada) ============================
/// UM PRECO POR CLASSE, em zeni, partindo dos 10 por turf do Recreator:
///   * a PAREDE custa esses 10;
///   * o PISO custa a metade: ele se usa aos montes (e o chao e o teto da casa -- uma sala de 4x4 leva 16), e
///     a 10 cada o miolo custaria mais que as paredes;
///   * a PORTA custa 50: e uma parede com tranca. No DM por senha numa porta pedia uma Key de 100 zeni
///     (`Tier 1.dm:8-13`); aqui a senha vem com a porta, por metade disso.
/// Em RECURSOS tudo custa o dobro (<see cref="Recurso.CustoEmRecursos"/>): 10, 20 e 100.
///
/// A CONTA DE QUEM COMECA: um abrigo de 5x5 por fora (15 paredes e 1 porta) sai por 200 zeni ou 400
/// recursos; com o piso do miolo (9), 245 ou 490. Dezenas por peca, e nao milhares -- e pra quem derruba
/// arvore e minera conseguir fechar quatro paredes.
/// ===================================================================================================
///
/// QUEM COBRA E O SERVIDOR (`GameServer.CustoDeObra.cs`), pelo `Cobrar` do contrato da economia. O cliente le
/// esta mesma tabela so pra mostrar o preco e pintar o fantasma de vermelho antes do clique.
/// </summary>
public static class PrecoDeBloco
{
	/// <summary>O que cada classe custa, EM ZENI. Em recursos e o dobro -- ver <see cref="Na"/>.</summary>
	public const double Piso = 5, Parede = 10, Porta = 50;

	/// <summary>
	/// A FRACAO DO QUE FOI PAGO QUE VOLTA PRO BOLSO de quem desmancha a PROPRIA peca. ZERO = nao volta nada,
	/// que e a regra de hoje: desmanchar nao reembolsa.
	///
	/// FICA AQUI, DESLIGADA, pra quem quiser o jeito do Minecraft (la o bloco quebrado volta inteiro): com 0.5
	/// volta a metade, NA MOEDA em que a peca foi paga -- e por isso cada bloco guarda o que custou
	/// (`BlocoDePe.Pago`). Sem essa anotacao o reembolso viraria cambio (paga em recurso, recebe em zeni) e as
	/// bases erguidas antes de haver preco, que nao custaram nada, passariam a render dinheiro.
	///
	/// E `static` e nao `const` porque a bancada (`--blocoteste`) a liga pra provar as duas coisas: que com ela
	/// em zero nada volta, e que ligada volta a conta certa.
	/// </summary>
	public static double DevolveAoDesmanchar = 0;

	/// <summary>
	/// O VERBO que troca a moeda (`C2S.Verbo`), com a <see cref="Chave"/> da moeda no argumento. A escolha mora
	/// no servidor, por jogador, e nao viaja em cada bloco: o `C2S.Bloco` continua com o formato de sempre.
	/// </summary>
	public const string Verbo = "obra_moeda";

	public static double EmZeni(ClasseDeBloco classe) => classe switch
	{
		ClasseDeBloco.Parede => Parede,
		ClasseDeBloco.Porta => Porta,
		_ => Piso,
	};

	/// <summary>O preco desta classe NA MOEDA escolhida: em zeni o da tabela; em recursos, o dobro.</summary>
	public static double Na(ClasseDeBloco classe, Moeda moeda) =>
		moeda == Moeda.Recursos ? Recurso.CustoEmRecursos(EmZeni(classe)) : EmZeni(classe);

	/// <summary>
	/// A moeda em texto: o argumento do <see cref="Verbo"/>, o que o `config.json` do cliente guarda e a palavra
	/// que o jogador le. Texto e nao o numero do enum, pra o arquivo nao depender da ordem dele.
	/// </summary>
	public static string Chave(Moeda moeda) => moeda == Moeda.Recursos ? "recursos" : "zeni";

	/// <summary>A moeda de uma <see cref="Chave"/>; nulo se o texto nao e de moeda nenhuma.</summary>
	public static Moeda? DaChave(string? chave) => chave?.Trim().ToLowerInvariant() switch
	{
		"zeni" => Moeda.Zeni,
		"recursos" => Moeda.Recursos,
		_ => null,
	};

	public static string NomeDaClasse(ClasseDeBloco classe) => classe switch
	{
		ClasseDeBloco.Parede => "parede",
		ClasseDeBloco.Porta => "porta",
		_ => "piso",
	};

	/// <summary>
	/// Uma quantia como o jogador a le: inteira e pra BAIXO. O saldo de recursos e fracionario (minerar rende
	/// por segundo), e arredondar pra cima mostraria "custa 20 e voce tem 20" a quem tem 19,6 e nao pode pagar.
	/// </summary>
	public static string Quantia(double quanto) => Math.Floor(Math.Max(0, quanto) + 1e-9).ToString("N0");

	/// <summary>"10 zeni", "20 recursos" -- o preco de uma classe na moeda escolhida.</summary>
	public static string Etiqueta(ClasseDeBloco classe, Moeda moeda) => $"{Quantia(Na(classe, moeda))} {Chave(moeda)}";

	/// <summary>A frase de quem nao tem saldo. Uma so, pras duas pontas: o fantasma diz o que o servidor diria.</summary>
	public static string FaltaSaldo(ClasseDeBloco classe, Moeda moeda, double saldo) =>
		(moeda == Moeda.Recursos ? "faltam recursos" : "falta zeni")
		+ $": {NomeDaClasse(classe)} custa {Quantia(Na(classe, moeda))} e você tem {Quantia(saldo)}.";
}
