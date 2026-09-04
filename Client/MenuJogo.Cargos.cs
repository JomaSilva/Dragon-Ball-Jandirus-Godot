using Godot;
using Jandirus.Core.Skills;
using Jandirus.Core.Social;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// A ABA CARGOS -- quem manda no mundo, e o que cada cargo entrega.
///
/// ============================ A LINGUA DA LEARNING, NESTA ABA ============================
/// Ela era uma fila de botoes de largura total com tres linhas de texto apagado embaixo de cada um
/// (foto de antes: `aba-08-cargos.png`), e o botao era tambem o titulo: cargo ocupado virava linha,
/// cargo vago virava botao, e "reivindicar" ficava escrito ate no botao apagado de quem nao podia.
/// Agora e UM CARTAO POR CARGO, em duas colunas: o nome no cabecalho com as pilulas de estado (VAGO
/// ou o dono; se voce cumpre os requisitos), a descricao como nota, "dá:" e "exige:" como linhas de
/// texto longo, e o botao "Reivindicar" SO no cargo vago que voce pode pegar. O aviso do fim virou
/// uma nota unica. O que a aba DIZ e o de antes: sai da mesma lista que o servidor manda.
/// ==========================================================================================
/// </summary>
public partial class MenuJogo
{
	// =====================================================================
	// CARGOS -- quem manda no mundo
	// =====================================================================
	/// <summary>
	/// A lista de cargos do MUNDO, com quem ocupa cada um e o que falta pra voce.
	///
	/// Mostra os OCUPADOS tambem: saber quem e o Guardiao da Terra e metade do valor de um
	/// sistema de cargos -- a outra metade e poder disputar quando vagar.
	/// </summary>
	private void AbaCargos()
	{
		if (GameClient.Instance is not { } cli) return;

		if (cli.Cargos.Count == 0)
		{
			Aviso("pedindo a lista ao servidor...");
			cli.SendCargo();   // a lista chega e o painel se redesenha sozinho
			return;
		}

		// ============================ A FAIXA: QUANTOS TRONOS ESTAO VAZIOS ============================
		// E o numero desta aba: quem abre Cargos quer saber o que da pra disputar. A legenda diz de
		// quantos, e quantos deles voce ja cumpre -- os tres saem da lista que o servidor mandou
		// (`Dono` vazio, `Falta` vazia, `Dono` = voce), e nao de conta nenhuma feita aqui.
		// ============================================================================================
		int vagos = 0, aptos = 0, seus = 0;
		foreach (GameClient.CargoInfo c in cli.Cargos)
		{
			if (c.Dono.Length == 0) vagos++;
			if (c.Falta.Length == 0) aptos++;
			if (c.Dono.Length > 0 && c.Dono == cli.LocalName) seus++;
		}
		Faixa("Cargos", $"{vagos} vago{(vagos == 1 ? "" : "s")}",
			  $"de {cli.Cargos.Count} no mundo  ·  você cumpre os requisitos de {aptos}"
			  + (seus > 0 ? $"  ·  {seus} {(seus == 1 ? "é seu" : "são seus")}" : ""));

		// DUAS COLUNAS, como a lista de arvores da Learning: sao ~30 cartoes, e em coluna unica a aba
		// vira um rolo de tres telas. Os textos longos ("dá:") quebram linha dentro da metade.
		GridContainer grade = Colunas();
		foreach (GameClient.CargoInfo c in cli.Cargos) CartaoDeCargo(cli, c, grade);

		Nota("Um cargo tem UM dono no mundo, e uma alma carrega UM cargo. A escada dos Kaios é a exceção: "
			 + "subir larga o cargo anterior.", _conteudo);
	}

	/// <summary>
	/// UM CARTAO DE CARGO: o nome, as pilulas de estado, a descricao, o que ele DA, o que ele EXIGE, e
	/// o botao de reivindicar quando (e so quando) o trono esta vago e voce cumpre os requisitos.
	///
	/// ============================ O QUE O CARGO E, E O QUE ELE DA ============================
	/// As duas linhas que faltavam. O painel mostrava trinta cargos e o jogador nao tinha como
	/// saber o que nenhum deles entrega -- nem antes de disputar, nem depois de perder. A
	/// descricao vem da `RankDef.Desc` e a dadiva da tabela que o servidor executa de verdade,
	/// **com o que ainda e botao mudo marcado** (ver `OQueOCargoEntrega`, no servidor).
	///
	/// ELAS SAEM PRO CARGO OCUPADO TAMBEM, e nao so pro vago: metade do valor de um sistema de
	/// cargos e saber o que o dono atual ganhou com ele.
	///
	/// "REIVINDICAR" SO EXISTE ONDE DA PRA APERTAR. O botao antigo aparecia apagado em todo cargo vago,
	/// com a palavra "reivindicar" escrita nele -- e um botao que diz o que nao faz ensina errado. O que
	/// falta pra poder apertar esta na linha "exige:", que e a informacao de verdade. Quem decide continua
	/// sendo o SERVIDOR: o clique manda a chave e ele confere tudo de novo.
	/// ==========================================================================================
	/// </summary>
	private void CartaoDeCargo(GameClient cli, GameClient.CargoInfo c, Control pai)
	{
		bool vago = c.Dono.Length == 0;
		bool apto = c.Falta.Length == 0;
		string nome = NomeDoCargo(c.Chave);

		VBoxContainer corpo = Cartao("", pai);
		MarcarCartaoDeItem(corpo, "cargo", nome);

		corpo.AddChild(Cabecalho(nome, vago && apto ? Tema.Destaque : Tema.Texto,
			vago ? ("VAGO", Tema.Bom) : (c.Dono, c.Dono == cli.LocalName ? Tema.Destaque : Tema.Texto),
			apto ? ("você cumpre os requisitos", Tema.Bom) : ("requisitos em falta", Tema.TextoFraco)));

		if (c.Desc.Length > 0) Nota(c.Desc, corpo);
		if (c.Da.Length > 0) LinhaDeTextoLongo("dá", c.Da, Tema.Texto, corpo);
		if (!apto) LinhaDeTextoLongo("exige", c.Falta, Tema.Perigo, corpo);

		if (!vago || !apto) return;

		var b = new Button
		{
			Text = "Reivindicar",
			TooltipText = "você cumpre os requisitos",
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		string chave = c.Chave;
		b.Pressed += () => cli.SendCargo(chave);
		corpo.AddChild(b);
	}

	private static string NomeDoCargo(string chave) =>
		Jandirus.Core.Ranks.Cargos.Get(chave)?.Nome ?? chave;

	/// <summary>
	/// O PEDACO DESTA ABA NA ASSINATURA DE CACHE (ver `MenuJogo.Assinatura`): tudo que ESTA aba
	/// desenha e que a assinatura basica (em MenuJogo.cs) nao cobre entra aqui, nos mesmos
	/// arredondamentos em que e desenhado.
	///
	/// A BASICA COBRE CHAVE, DONO E "O QUE FALTA". Esta pagina tambem ESCREVE a descricao e o "dá:" de
	/// cada cargo (que so mudam se o servidor mandar outra lista) e o nome de quem ve (a pilula do dono
	/// e o "é seu" da faixa). Os textos entram como COMPRIMENTO somado, e nao inteiros: sao ate 600
	/// caracteres por cargo, e uma assinatura que se compara cinco vezes por segundo nao pode custar
	/// mais que a remontagem que ela evita.
	/// </summary>
	private string ExtraDaAssinaturaDeCargos(SheetState f)
	{
		if (GameClient.Instance is not { } c) return "";
		int letras = 0;
		foreach (GameClient.CargoInfo k in c.Cargos) letras += k.Desc.Length + k.Da.Length;
		return $"{letras}|{c.LocalName}";
	}
}
