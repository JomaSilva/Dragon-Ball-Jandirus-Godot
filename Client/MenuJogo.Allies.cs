using Godot;
using Jandirus.Core.Npc;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// (2026-10-21) A ABA "ALLIES" -- o pedido dos jogadores: *"eles iriam pra uma aba nova chamada allies pra n confundir com people"*. A
/// People continua sendo a de quem voce conhece (gente de verdade, NUNCA NPC); esta e a dos habitantes: os que ja sao seus aliados e as
/// amizades em andamento.
///
/// ELA SO EXISTE PRA QUEM TEM O QUE MOSTRAR -- ao menos um aliado ou uma amizade em andamento --, e quem decide e o servidor, pelo que
/// manda (`S2C.Aliados`, `GameClient.Aliados`): vazio, a aba nao entra na barra. Mora logo depois da People (ou da Alunos/Mestre).
///
///   * UM CARTAO POR ALIADO: nome, raca, de onde veio, o nivel (com a experiencia), o poder (ESCRITO pelo servidor: em numero so com
///     medidor), a relacao, e as ordens -- soltar ou guardar, seguir ou ficar, atacar ou matar o alvo marcado, parar, os dois reflexos, as
///     melhorias por gastar e dispensar (dois cliques).
///   * AS AMIZADES EM ANDAMENTO: o medidor de cada habitante, as mais perto de encher primeiro.
///
/// O BOTAO SO PEDE: tudo e conferido de novo no servidor (`GameServer.Aliados.OrdemAoAliado`).
/// </summary>
public partial class MenuJogo
{
	/// <summary>O nome da aba (o do dono). As bancadas a procuram por aqui.</summary>
	internal const string AbaDosAliados = Aliados.NomeDaAba;

	/// <summary>Os rotulos dos botoes do cartao -- o robo `--diagaliados` os aperta por aqui.</summary>
	internal const string RotuloSoltarAliado = "Soltar", RotuloGuardarAliado = "Guardar", RotuloFicarAliado = "Ficar",
						  RotuloSeguirAliado = "Seguir", RotuloAtacarAliado = "Atacar o alvo", RotuloMatarAliado = "Matar o alvo",
						  RotuloPararAliado = "Parar", RotuloDispensarAliado = "Dispensar";

	/// <summary>O titulo do cartao das amizades em andamento.</summary>
	internal const string TituloDasAmizades = "Amizades em andamento";

	/// <summary>A largura minima de um cartao de aliado: cabe no menu minimo, e dois cabem lado a lado na tela larga.</summary>
	private const float LarguraMinimaDoCartaoDeAliado = 380f;

	/// <summary>Quanto tempo o segundo clique de "Dispensar" fica valendo, em segundos (o mesmo gesto sem volta da aba Alunos).</summary>
	private const double SegundosPraDispensar = 4;

	private int _aliadoADispensar;
	private ulong _dispensarAteMs;

	private bool DispensaArmada => _aliadoADispensar != 0 && Time.GetTicksMsec() < _dispensarAteMs;

	/// <summary>
	/// POE A ABA NA BARRA se o servidor mandou algo pra ela, logo depois da People (ou da Alunos/Mestre). E o OUVIDO nasce aqui, na primeira
	/// vez que o menu monta a barra: a aba aparece e se refaz quando o pacote chega, mesmo sem ficha nova (ver `OuvidoDosAliados`).
	/// </summary>
	private void PorAAbaDosAliados(List<string> abas)
	{
		if (GetNodeOrNull(OuvidoDosAliados.Nome) == null) AddChild(new OuvidoDosAliados { Name = OuvidoDosAliados.Nome });
		if (GameClient.Instance is not { } cli || cli.Aliados.Vazio) return;
		int depois = abas.IndexOf(AbaDeAlunos);
		if (depois < 0) depois = abas.IndexOf(AbaDeMestre);
		if (depois < 0) depois = abas.IndexOf("People");
		abas.Insert(depois >= 0 ? depois + 1 : abas.Count, AbaDosAliados);
	}

	/// <summary>A aba chegou (ou saiu): a barra e a pagina se refazem com o menu aberto -- a assinatura decide se a pagina muda.</summary>
	internal void AoAliados() { if (Visible) Redesenhar(); }

	/// <summary>O que a pagina assina: a busca, a versao do pacote e o "Dispensar" armado.</summary>
	private string ExtraDaAssinaturaDeAliados(SheetState _) =>
		$"{_busca.Text.Trim()}|{GameClient.Instance?.VersaoDosAliados}|{(DispensaArmada ? _aliadoADispensar : 0)}";

	private void AbaAliados()
	{
		if (GameClient.Instance is not { } cli) return;
		AliadosState s = cli.Aliados;
		int soltos = s.Aliados.Count(a => (a.Estado & EstadoDeAliado.Solto) != 0);
		Faixa("Aliados", $"{s.Aliados.Count} de {s.Teto}",
			  $"ao seu lado: {soltos} de {s.TetoDeSoltos} (as criaturas de cápsula contam junto) · as ordens de cada um ficam no cartão dele");

		if (s.Aliados.Count == 0)
			Nota("Você ainda não recrutou ninguém. Quando a amizade com um habitante chegar a 100, a tecla E perto dele mostra \"Recrutar\".",
				 Cartao(""));
		else
		{
			var fila = new HFlowContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
			fila.AddThemeConstantOverride("h_separation", 8);
			fila.AddThemeConstantOverride("v_separation", 8);
			_conteudo.AddChild(fila);
			foreach (AliadoNaAba a in s.Aliados) CartaoDoAliado(cli, a, fila);
		}

		VBoxContainer am = Cartao(TituloDasAmizades);
		if (s.Amizades.Count == 0) Nota("Nenhuma amizade em andamento.", am);
		foreach (AmizadeNaAba z in s.Amizades)
		{
			bool cheia = z.Pontos >= Amizade.Cheia;
			LinhaComBarra($"{z.Nome} ({Jandirus.Core.Races.NomeDaRaca.Bonito(z.Raca)}, {z.Onde})", $"{z.Pontos} / {Amizade.Cheia:0}",
						  z.Pontos / Amizade.Cheia, cheia ? Tema.Bom : Tema.Ki, am, cheia ? Tema.Bom : null);
			if (cheia) am.AddChild(Pilulas(("PRONTO PRA RECRUTAR", Tema.Bom)));
		}
		Nota("Perto de um habitante, a tecla E abre o menu dele: \"Dar presente\" e \"Treinar\" aproximam vocês -- o mesmo dia rende cada "
			 + "vez menos. Com 100, aparece \"Recrutar\". Bater nele de verdade zera a amizade; e se ele morrer, ela se perde.", am);
	}

	/// <summary>O cartao de um aliado: quem ele e, como esta, e as ordens.</summary>
	private void CartaoDoAliado(GameClient cli, AliadoNaAba a, Control fila)
	{
		bool solto = (a.Estado & EstadoDeAliado.Solto) != 0;
		bool ficando = (a.Estado & EstadoDeAliado.Ficando) != 0;
		VBoxContainer c = Cartao("", fila);
		if (c.GetParent() is PanelContainer card)
		{
			card.CustomMinimumSize = new Vector2(LarguraMinimaDoCartaoDeAliado, 0);
			card.SetMeta("aliado", a.Id);
			card.SetMeta("titulo", a.Nome);
		}

		c.AddChild(Cabecalho(a.Nome, Tema.Texto,
			(solto ? "AO SEU LADO" : "EM CASA", solto ? Tema.Bom : Tema.TextoFraco),
			(solto ? (ficando ? "FICANDO" : "SEGUINDO") : "", Tema.Ki),
			((a.Estado & EstadoDeAliado.Lutando) != 0 ? "LUTANDO" : "", Tema.Destaque),
			((a.Estado & EstadoDeAliado.Matando) != 0 ? "MATAR" : "", Tema.Perigo),
			((a.Estado & EstadoDeAliado.Caido) != 0 ? "CAÍDO" : "", Tema.Perigo)));
		Linha("Raça", Jandirus.Core.Races.NomeDaRaca.Bonito(a.Raca), null, c);
		if (a.Planeta.Length > 0) Linha("Morava em", a.Planeta, null, c);
		bool noTeto = a.Nivel >= Jandirus.Core.Tech.Companheiros.NivelMaximo;
		LinhaComBarra("Nível", noTeto ? $"{a.Nivel} (o máximo)" : $"{a.Nivel} de {Jandirus.Core.Tech.Companheiros.NivelMaximo} · {a.Exp} de {a.ExpDoNivel} de experiência",
					  noTeto ? 1 : a.Exp / (double)Math.Max(1, a.ExpDoNivel), Tema.Destaque, c);
		Linha("Poder", a.Poder, null, c);
		Linha("Relação", $"{a.Relacao}", null, c);

		string id = $"{a.Id}";
		var ordens = new HFlowContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		ordens.AddThemeConstantOverride("h_separation", 4);
		ordens.AddThemeConstantOverride("v_separation", 4);
		c.AddChild(ordens);
		Ordem(ordens, solto ? RotuloGuardarAliado : RotuloSoltarAliado, solto ? Aliados.VerboGuardar : Aliados.VerboSoltar, id, a.Id,
			  solto ? "ele volta pra casa: chame-o de novo daqui" : "ele vem pra perto de você e passa a seguir você");
		if (solto)
		{
			Ordem(ordens, ficando ? RotuloSeguirAliado : RotuloFicarAliado, Aliados.VerboSeguir, id, a.Id,
				  ficando ? "volta a seguir você" : "fica onde está até você mandar seguir");
			Ordem(ordens, RotuloAtacarAliado, Aliados.VerboAtacar, id, a.Id, "parte pra cima do alvo que você marcou (duplo clique), a até cinco tiles de você");
			Ordem(ordens, RotuloMatarAliado, Aliados.VerboMatar, id, a.Id, "o mesmo, com o golpe letal -- ele não para no nocaute");
			Ordem(ordens, RotuloPararAliado, Aliados.VerboParar, id, a.Id, "larga a luta e volta pra você");
		}

		var reflexos = new HFlowContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		reflexos.AddThemeConstantOverride("h_separation", 4);
		c.AddChild(reflexos);
		bool ataco = (a.Estado & EstadoDeAliado.QuandoAtaco) != 0, apanho = (a.Estado & EstadoDeAliado.QuandoApanho) != 0;
		Ordem(reflexos, $"Ataca quem você ataca: {(ataco ? "sim" : "não")}", Aliados.VerboQuandoAtaco, id, a.Id,
			  "pede a relação acima de 20");
		Ordem(reflexos, $"Ataca quem bate em você: {(apanho ? "sim" : "não")}", Aliados.VerboQuandoApanho, id, a.Id,
			  "pede a relação acima de 20");

		if (a.Sobras > 0)
		{
			Linha("Melhorias por gastar", $"{a.Sobras}", Tema.Destaque, c);
			var melhorias = new HFlowContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
			melhorias.AddThemeConstantOverride("h_separation", 4);
			c.AddChild(melhorias);
			Ordem(melhorias, "Físicos", Aliados.VerboMelhorar, $"{a.Id}/1", a.Id, "ataque e defesa físicos");
			Ordem(melhorias, "Ki", Aliados.VerboMelhorar, $"{a.Id}/2", a.Id, "ataque, defesa e perícia de Ki");
			Ordem(melhorias, "Variados", Aliados.VerboMelhorar, $"{a.Id}/3", a.Id, "técnica, velocidade e magia");
			Ordem(melhorias, "Poder", Aliados.VerboMelhorar, $"{a.Id}/4", a.Id, "o poder sobe pela média do mundo");
		}

		// DISPENSAR E SEM VOLTA: o primeiro clique arma, o segundo (dentro de alguns segundos) manda.
		bool armado = DispensaArmada && _aliadoADispensar == a.Id;
		var dispensar = new Button
		{
			Text = armado ? $"Confirmar: dispensar {a.Nome}" : RotuloDispensarAliado,
			FocusMode = Control.FocusModeEnum.None,
			TooltipText = "ele volta pra vida dele e deixa de ser seu aliado -- sem volta",
		};
		dispensar.SetMeta("aliado", a.Id);
		if (armado) dispensar.AddThemeColorOverride("font_color", Tema.Perigo);
		dispensar.Pressed += () =>
		{
			if (DispensaArmada && _aliadoADispensar == a.Id)
			{
				_aliadoADispensar = 0;
				cli.SendVerbo(Aliados.VerboDispensar, id);
			}
			else
			{
				_aliadoADispensar = a.Id;
				_dispensarAteMs = Time.GetTicksMsec() + (ulong)(SegundosPraDispensar * 1000);
			}
			Redesenhar();
		};
		c.AddChild(dispensar);
	}

	/// <summary>Um botao de ordem: manda o verbo com o argumento, e leva o id do aliado em metadado (o robo acha o botao do cartao certo).</summary>
	private static void Ordem(Control pai, string rotulo, string verbo, string arg, int aliado, string dica)
	{
		var b = new Button { Text = rotulo, FocusMode = Control.FocusModeEnum.None, TooltipText = dica };
		b.SetMeta("aliado", aliado);
		b.Pressed += () => GameClient.Instance?.SendVerbo(verbo, arg);
		pai.AddChild(b);
	}
}

/// <summary>
/// O OUVIDO DA ABA ALLIES: um no filho do menu que assina o `AliadosMudaram` e refaz o menu quando o pacote chega -- a aba entra na barra
/// (ou sai dela) e a pagina se refaz mesmo sem ficha nova. POR QUE UM NO (o argumento do `OuvidoDoServicoDoAlem`): o menu se redesenha a
/// cada pacote de FICHA, e a ficha so sai quando muda; e a assinatura tem de sair quando o menu sai -- o `GameClient` sobrevive ao logout.
/// </summary>
internal partial class OuvidoDosAliados : Node
{
	internal const string Nome = "OuvidoDosAliados";

	/// <summary>DEFEITO INJETADO (robo `--diagaliados`): o ouvido nao refaz a aba -- o pacote novo so viraria tela na proxima ficha.</summary>
	internal static bool SurdoDeTeste;

	public override void _Ready()
	{
		if (GameClient.Instance is { } cli) cli.AliadosMudaram += Ouvir;
	}

	public override void _ExitTree()
	{
		if (GameClient.Instance is { } cli) cli.AliadosMudaram -= Ouvir;
	}

	private void Ouvir()
	{
		if (!SurdoDeTeste) (GetParent() as MenuJogo)?.AoAliados();
	}
}
