using Godot;
using Jandirus.Core.Skills;

namespace Jandirus.Client;

/// <summary>
/// A familia de provas da aba Nav da `--diagabas` (F12). Ver o cabecalho de `RoboDasAbas.cs`.
///
/// ============================ O CONTRATO PRIMEIRO, A MOLDURA DEPOIS ============================
/// A bancada nasce SEM o Nav System, e a primeira coisa que se cobra e que a aba NAO exista (o
/// portao do `Abas()`, pedido do dono: a aba so aparece com o item na mochila). O portao inteiro e
/// provado pela `--diagnav`; aqui ele e o contrato que o redesenho nao pode ter quebrado.
///
/// A MOLDURA (a barra de zoom num cartao, as legendas num cartao, o mapa FORA de cartao) so da pra
/// olhar com o item -- e o item so entra pela mochila do servidor. Quando o servidor mora neste
/// processo (`--host`), a bancada o poe e tira pelo mesmo gancho da `--diagnav`
/// (`NavSystemNaMochilaDeTeste`); com o servidor DEDICADO (o normal do `testar-as-abas.bat`) nao ha
/// como, e isso vai pro placar de nao-medidos em vez de virar verde por omissao.
/// ============================================================================================
/// </summary>
public partial class RoboDasAbas
{
	private async System.Threading.Tasks.Task F12_Nav(MenuJogo menu, GameClient cli)
	{
		Nota("--- F12: Nav: ausente sem o item (contrato); com o item, a moldura em cartoes e o mapa fora deles ---");

		// ---------------- 1. O CONTRATO: sem o item, nada de aba ----------------
		Checa("contrato: sem o Nav System na mochila a aba Nav NAO esta na barra",
			  Array.IndexOf(menu.AbasDeTeste, "Nav") < 0, string.Join(",", menu.AbasDeTeste));
		Checa("...porque o bit Nav nao chega no pacote e a mochila nao tem o item",
			  !cli.Atributos.Tem(Jandirus.Net.Protocol.Poder.Nav)
			  && cli.Mochila.Quantos(Jandirus.Core.Items.CatalogoDeItens.NavSystem) == 0);
		Checa("...e nao ha pagina 'Nav' visivel", menu.PaginaDeTeste("Nav") is not { Visible: true });

		// ---------------- 2. A MOLDURA, so com o servidor neste processo ----------------
		// `GameServer.Instance` EXISTE SEMPRE (e um node da cena, ligado so pelo `Start` do `--host`), entao
		// a pergunta certa e a flag: a primeira rodada achou a instancia no cliente que DISCA, mandou o
		// item pra um `_players` vazio e reprovou uma aba que nao tinha como montar.
		bool hospedando = Array.IndexOf(OS.GetCmdlineArgs(), "--host") >= 0;
		if (!hospedando || Jandirus.Server.GameServer.Instance is not { } srv)
		{
			_pulados++;
			_naoMedidos.Add("Nav com o item: a moldura em cartoes (o servidor mora noutro processo -- rode a --diagabas com --host pra esta parte)");
			Nota("  PULADA  a aba Nav com o item   [servidor dedicado: nao ha como por o Nav System na mochila daqui; com --host esta parte roda]");
			await System.Threading.Tasks.Task.CompletedTask;
			return;
		}

		bool ganhou = srv.NavSystemNaMochilaDeTeste(cli.LocalId, true);
		bool acendeu = await Ate(() => cli.Atributos.Tem(Jandirus.Net.Protocol.Poder.Nav), 8);
		Checa("com o item na mochila, o bit Nav chega pelo fio", ganhou && acendeu);
		menu.IrPara("Nav");
		await Quadros(4);
		Control? pg = menu.PaginaDeTeste("Nav");
		Checa("...e a aba Nav monta", Array.IndexOf(menu.AbasDeTeste, "Nav") >= 0 && pg is { Visible: true });
		if (pg is { Visible: true })
		{
			Checa("a barra de zoom mora num cartao com o titulo da carta, com os 4 botoes de camera de sempre",
				  CartaoComTitulo(pg, "Carta estelar (em terra: só leitura)") is { } carta
				  && Botao(carta, "+") != null && Botao(carta, "-") != null
				  && Botao(carta, "centralizar em mim") != null && Botao(carta, "ver tudo") != null);
			Checa("as legendas moram num cartao 'Legenda', e a da escala (Terra->Namek, min) continua escrita",
				  CartaoComTitulo(pg, "Legenda") is { } leg
				  && Rotulos(leg).Any(l => l.Text.Contains("Namek", StringComparison.Ordinal) && l.Text.Contains("min", StringComparison.Ordinal)));
			Checa("o MAPA fica fora de qualquer cartao (a --diagnav sobe do mapa ate o primeiro PanelContainer, que tem que ser o painel do menu)",
				  menu.MapaDeTeste is { } mapa && Ancestral<PanelContainer>(mapa) is { } p && !p.HasMeta("cartao"));
			Checa("contrato --diagembarque: nenhum botao da Nav fala de nave", SemPalavraDeNaveNosRotulos(RotulosEBotoes(pg)));

			if (Ancestral<ScrollContainer>(pg) is { } rol) rol.ScrollVertical = 0;
			await Quadros(2);
			Image? foto = await Foto();
			await Guardar("nav-01-com-o-item", foto);
			ChapaDoCartaoNaPaleta("a chapa do cartao da carta e a clara do tema (Tema.PainelClaro)", foto, CartaoComTitulo(pg, "Carta estelar (em terra: só leitura)"), Tema.PainelClaro);
		}

		srv.NavSystemNaMochilaDeTeste(cli.LocalId, false);
		bool apagou = await Ate(() => !cli.Atributos.Tem(Jandirus.Net.Protocol.Poder.Nav), 8);
		menu.IrPara("Stats");
		await Quadros(3);
		Checa("tirado o item, a aba Nav some de novo (o estado volta ao que a bancada nasceu)",
			  apagou && Array.IndexOf(menu.AbasDeTeste, "Nav") < 0);
	}
}
