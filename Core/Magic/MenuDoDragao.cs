namespace Jandirus.Core.Magic;

/// <summary>De que dragao e o menu que a janela de desejos mostra.</summary>
public enum QualDragao : byte
{
	/// <summary>
	/// Nenhum -- ou "o que couber aqui", quando e o PEDIDO de um menu: o dragao de pe de quem pede, o Super
	/// Shenron no espaco, o dragao da estatua do planeta em terra.
	/// </summary>
	Nenhum = 0,

	/// <summary>O dragao das sete esferas de um planeta: Shenron, Porunga.</summary>
	Comum = 1,

	/// <summary>O Super Shenron das sete Super Esferas.</summary>
	Super = 2,
}

/// <summary>
/// OS GESTOS EM VOLTA DO DRAGAO que nao sao desejo -- as linhas de <see cref="EntradaDoDragao.Acao"/>. O id e o
/// que a aba World manda pra janela ja abrir na linha certa (<see cref="MenuDoDragao.Foco"/>), e e por ele que
/// as bancadas acham a linha.
/// </summary>
public static class AcoesDoDragao
{
	/// <summary>Mandar o dragao embora sem pedir -- o "Cancel" do DM (`WishTable.dm:156-158`).</summary>
	public const string Dispensar = "dispensar";

	/// <summary>Quantos pedidos a estatua da por invocacao -- o `Redo()` do criador (`Dragonballs.dm:59-89`).</summary>
	public const string Pedidos = "pedidos";

	/// <summary>Derrubar a estatua -- `Destroy_Statue()` (`Dragonballs.dm:50-58`).</summary>
	public const string Derrubar = "derrubar";

	/// <summary>Trocar a janela pro menu do Super Shenron (quem tem assunto com ele e esta num planeta).</summary>
	public const string Super = "super";

	/// <summary>Emprestar as sete Super Esferas a quem fala a lingua (`ProceduralSpace.dm:1651-1680`).</summary>
	public const string Emprestar = "emprestar";

	/// <summary>Tomar de volta as sete emprestadas.</summary>
	public const string Retomar = "retomar";

	public const string AceitarGuarda = "guarda_aceitar";
	public const string RecusarGuarda = "guarda_recusar";

	/// <summary>O porta-voz dizendo o pedido de quem emprestou.</summary>
	public const string Pronunciar = "pronunciar";

	/// <summary>A resposta ao desejo que cobra a vida -- o `alert(alvo, ..., "ACEITO O PRECO", "Recusar")` (`:1632`).</summary>
	public const string Preco = "preco";

	public const string RecusarPreco = "preco_recusar";
}

/// <summary>
/// UMA RESPOSTA DA SEGUNDA ETAPA -- "Para mim", uma pessoa da lista, um mundo destruido, "3 pedidos".
/// </summary>
public sealed class EscolhaDoDragao
{
	/// <summary>
	/// O ARGUMENTO INTEIRO do verbo quando esta e a resposta ("matar #57", "juventude extremo"). Quem o escreve e
	/// o servidor e quem o confere, de novo, e o servidor: a janela so o devolve.
	/// </summary>
	public string Pedido = "";

	/// <summary>O que o jogador le no botao: "Para mim", o nome da pessoa.</summary>
	public string Rotulo = "";

	/// <summary>A linha miuda embaixo: a raca, "seu alvo marcado", o motivo de estar apagada.</summary>
	public string Nota = "";

	public bool Pode = true;

	/// <summary>E o alvo que o jogador tem MARCADO (duplo clique): a janela o poe logo abaixo do "Para mim".</summary>
	public bool Marcado;
}

/// <summary>UMA LINHA DO MENU -- um desejo, ou um gesto em volta do dragao.</summary>
public sealed class EntradaDoDragao
{
	/// <summary>A identidade da linha: o id do desejo (<see cref="DesejoDef.Id"/>) ou um dos <see cref="AcoesDoDragao"/>.</summary>
	public string Id = "";

	/// <summary>O verbo de producao que a janela manda quando esta linha e pedida ("db_desejar", "sdb_invocar"...).</summary>
	public string Verbo = "";

	/// <summary>O argumento do verbo quando a linha NAO tem segunda etapa.</summary>
	public string Pedido = "";

	public string Nome = "";

	/// <summary>Uma linha do que faz.</summary>
	public string Desc = "";

	public bool Pode = true;

	/// <summary>Com <see cref="Pode"/> falso: por que nao. Com ele verdadeiro: um aviso que a janela mostra junto.</summary>
	public string Motivo = "";

	/// <summary>O titulo da segunda etapa ("Juventude pra quem?"). Vazio = a linha vai direto pra confirmacao.</summary>
	public string Pergunta = "";

	/// <summary>Nao e desejo: e um gesto em volta do dragao, e a janela o desenha noutra secao.</summary>
	public bool Acao;

	/// <summary>Custa caro e nao tem volta (a vida, as sete pra sempre): a janela pinta de vermelho.</summary>
	public bool Perigo;

	/// <summary>Sai no clique, sem a etapa de confirmar: recusar uma oferta, trocar de menu.</summary>
	public bool Direto;

	public List<EscolhaDoDragao> Escolhas = [];

	/// <summary>A linha tem segunda etapa?</summary>
	public bool TemPergunta => Pergunta.Length > 0;
}

/// <summary>
/// **O MENU DO DRAGAO** -- o que a janela de desejos de UM jogador mostra agora. Dado puro: quem o monta e o
/// servidor (`GameServer.MenuDoDragao.cs`), quem o desenha e o cliente (`Client/MenuDeDesejos.cs`), e nenhuma
/// regra de desejo mora aqui.
///
/// O dono (2026-10-10): *"ao invocar o dragao das esferas, deveria aparecer um menu com varios desejos q vc pode
/// escolher e caso tenha um target o jogo vai perguntar se e pra vc ou pra outra pessoa q vc pode escolher e nao
/// ter q abrir um menu e colocar um id de pedido"*. O DM faz isso com `input() in WishList` e `input() in
/// deadlist` (`WishTable.dm:150` e `:225`): duas caixas modais, a lista e o alvo. Aqui as duas caixas viram as
/// duas etapas de uma janela, e as listas chegam prontas.
/// </summary>
public sealed class MenuDoDragao
{
	public QualDragao Dragao;

	/// <summary>O servidor PEDE a janela agora (o dragao acabou de subir, o jogador apertou o botao da aba).</summary>
	public bool Abrir;

	/// <summary>O dragao que este jogador olhava foi embora: a janela fecha. So <see cref="Dragao"/> acompanha.</summary>
	public bool Fechar;

	/// <summary>Da pra pedir AGORA: o dragao esta de pe e e comigo.</summary>
	public bool DePe;

	/// <summary>"SHENRON", "PORUNGA", "SUPER SHENRON".</summary>
	public string Nome = "";

	public string Titulo = "";

	/// <summary>A frase do alto: quem pede, ou por que ninguem pode pedir agora.</summary>
	public string Estado = "";

	/// <summary>Pedidos que restam nesta ativacao, e quantos ela da. 0 de 0 = nao ha contagem pra mostrar.</summary>
	public int Restam, Total;

	/// <summary>Quanto o dragao ainda espera, em segundos. 0 = nao ha prazo correndo.</summary>
	public int Segundos;

	/// <summary>A linha em que a janela ja abre (o botao "Derrubar a estatua" da aba World, o preco a aceitar).</summary>
	public string Foco = "";

	public List<EntradaDoDragao> Entradas = [];

	public EntradaDoDragao? Entrada(string id) => Entradas.Find(e => e.Id == id);

	/// <summary>
	/// TUDO O QUE A JANELA DESENHA, num texto so. O servidor guarda o do ultimo menu que mandou a cada um e so
	/// manda outro quando este muda; a janela guarda o do que desenhou e so se refaz quando ele muda.
	///
	/// FICAM DE FORA <see cref="Segundos"/> (anda sozinho: a janela conta no relogio dela), <see cref="Abrir"/> e
	/// <see cref="Foco"/> (sao ordem de um envio, e nao estado).
	/// </summary>
	public string Assinatura()
	{
		var s = new System.Text.StringBuilder(512);
		s.Append((int)Dragao).Append('|').Append(DePe ? 1 : 0).Append('|').Append(Nome).Append('|').Append(Titulo)
		 .Append('|').Append(Estado).Append('|').Append(Restam).Append('/').Append(Total);
		foreach (EntradaDoDragao e in Entradas)
		{
			s.Append("\n").Append(e.Id).Append('|').Append(e.Verbo).Append('|').Append(e.Pedido).Append('|').Append(e.Nome)
			 .Append('|').Append(e.Desc).Append('|').Append(e.Pode ? 1 : 0).Append('|').Append(e.Motivo)
			 .Append('|').Append(e.Pergunta).Append('|').Append(e.Acao ? 1 : 0).Append(e.Perigo ? 1 : 0).Append(e.Direto ? 1 : 0);
			foreach (EscolhaDoDragao c in e.Escolhas)
				s.Append("\n  ").Append(c.Pedido).Append('|').Append(c.Rotulo).Append('|').Append(c.Nota)
				 .Append('|').Append(c.Pode ? 1 : 0).Append(c.Marcado ? 1 : 0);
		}
		return s.ToString();
	}
}
