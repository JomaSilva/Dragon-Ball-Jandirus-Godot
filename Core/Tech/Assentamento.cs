using Jandirus.Core.World;

namespace Jandirus.Core.Tech;

/// <summary>Por que um ponto nao serve pra assentar. Tipado, como as recusas de skill e de obra.</summary>
public enum RecusaDeAssento
{
	Pode,

	/// <summary>Isto nao vai no chao -- e coisa de vestir/carregar. A regra 2 do dono.</summary>
	NaoEDoChao,

	LongeDemais,
	DentroDeParede,
	EmCimaDagua,
	EmCimaDeNuvem,
	BeiradaDoMapa,

	/// <summary>Ja tem construcao ou nave neste pedaco de chao.</summary>
	LugarOcupado,
}

/// <summary>
/// ONDE DA PRA ASSENTAR UMA CONSTRUCAO -- a pergunta, escrita UMA vez, pras DUAS pontas.
///
/// ============================ POR QUE ISTO PRECISOU SAIR DOS DOIS LADOS ============================
/// O fantasma no mouse e o `Posicionar` do servidor faziam a mesma pergunta com respostas
/// diferentes, e o resultado era o modo de falha que o pedido do dono manda evitar: **o jogador
/// clica e nada acontece, sem explicacao**.
///
/// O placar medido antes desta classe existir:
///
/// | pergunta                | servidor            | fantasma           |
/// |-------------------------|---------------------|--------------------|
/// | longe demais            | sim (96 px)         | sim, com `96` DIGITADO A MAO |
/// | dentro de parede        | sim                 | sim                |
/// | em cima d'agua          | sim, **por acaso**  | **nao**            |
/// | em cima de nuvem        | so onde ela BARRA   | **nao**            |
/// | beirada do mapa         | **nao**             | **nao**            |
/// | ja tem coisa ali        | sim                 | **nao**            |
/// | e um item de vestir?    | **nao**             | **nao**            |
///
/// A linha da agua era a pior das sete: o fantasma ficava BRANCO em cima do lago, o jogador
/// clicava, e o servidor recusava dizendo *"nao da pra assentar dentro de uma parede"* -- previa
/// mentindo e mensagem errada, as duas de uma vez. (A recusa da agua era acidental: o
/// `MoveRules.Occupied` passa `ModoDeTravessia.APe`, e `ClasseDeAgua.Bloqueia(APe)` e verdadeiro.
/// Ninguem escreveu "nao se constroi na agua" em lugar nenhum.)
///
/// Com a pergunta aqui, as duas pontas leem a MESMA funcao e o desacordo que sobra e so o que a
/// rede impoe -- ver <see cref="Alcance"/>.
/// ==================================================================================================
///
/// ============================ E ELA PERGUNTA PELA CELULA, E NAO PELA CAIXA DO CORPO ============================
/// O servidor usava `MoveRules.Occupied`, que testa as quatro quinas de uma caixa de CORPO. Mas uma
/// construcao nao e um corpo: ela ocupa UMA celula, sempre, por maior que seja o desenho -- e a
/// regra de qual celula ja tem dono (<see cref="CatalogoDeObras.Celula"/>). Perguntar pela caixa do
/// corpo recusava celulas livres so porque a caixa encostava na parede vizinha, e -- pior -- dava
/// uma resposta que o fantasma, que desenha por CELULA, nao tinha como reproduzir.
///
/// Quem responde agora e <see cref="ZoneCollision.ServeDeChao"/>, que ja e a pergunta do pouso e do
/// berco: nao e parede, nao e beirada, nao e agua, nao e nuvem. Uma funcao, quatro recusas, e as
/// mesmas em toda a casa.
/// ==========================================================================================================
/// </summary>
public static class Assentamento
{
	/// <summary>
	/// A QUE DISTANCIA DA PRA ASSENTAR. Tres tiles -- o braco, e nao a vista.
	///
	/// ============================ O NUMERO ESTAVA ESCRITO DUAS VEZES ============================
	/// `AlcanceDePosicionar = 96f` no servidor e um `96` digitado no `_Process` do fantasma. Duas
	/// copias de um numero que precisa ser o mesmo e a definicao da armadilha "regra ligada em um
	/// chamador e esquecida no outro" -- bastava alguem afrouxar o servidor pra o fantasma ficar
	/// vermelho em lugar valido, ou apertar pra ele ficar branco em lugar recusado.
	///
	/// **O DESACORDO QUE SOBRA E DE REDE, E ELE E NORMAL.** O cliente mede da posicao DESENHADA (que
	/// e interpolada) e o servidor da `pl.Pos` (que e a ultima confirmada). Na beirada exata dos 96
	/// px os dois discordam por alguns pixels durante um passo. Nao da pra eliminar -- da pra
	/// TRATAR, e o tratamento e o da regra 4: o item nunca sai da mochila antes do aceite, o
	/// servidor diz o motivo, e o fantasma volta pra mao pra tentar um tile ao lado.
	/// ========================================================================================
	/// </summary>
	public const float Alcance = 96f;

	/// <summary>
	/// MEIO TILE DE FOLGA entre duas coisas assentadas. Duas construcoes no mesmo lugar viram uma so
	/// na tela e as duas respondem ao mesmo clique.
	/// </summary>
	public const float FolgaEntreObras = 24f;

	/// <summary>
	/// O LUGAR SERVE? -- so o que DEPENDE DO MAPA e da distancia, que e o que as duas pontas sabem.
	///
	/// O que fica de fora e o que so o servidor tem: a lista de obras e naves (ver
	/// <see cref="TemCoisaEm"/>, que o cliente tambem chama com a lista que ele recebeu) e as regras
	/// de posse. Ver `GameServer.Posicionar`.
	/// </summary>
	public static RecusaDeAssento DoLugar(ZoneCollision? mapa, Vec2 dePos, int cx, int cy)
	{
		// A CAIXA E QUADRADA (Chebyshev) e nao redonda -- e a conta que o servidor sempre fez, e
		// trocar por distancia euclidiana mudaria o alcance nas diagonais sem ninguem ter pedido.
		Vec2 centro = new(cx * ZoneCollision.TileSize + ZoneCollision.TileSize / 2f,
						  cy * ZoneCollision.TileSize + ZoneCollision.TileSize / 2f);
		if (Math.Abs(centro.X - dePos.X) > Alcance || Math.Abs(centro.Y - dePos.Y) > Alcance)
			return RecusaDeAssento.LongeDemais;

		// SEM MAPA NAO HA O QUE CONFERIR. Acontece em zona gerada que ainda nao carregou e no
		// cliente antes do primeiro pacote de colisao -- e "nao sei" nao pode virar "nao pode",
		// senao o fantasma fica vermelho no mundo inteiro por um instante depois de cada viagem.
		if (mapa == null) return RecusaDeAssento.Pode;

		if (mapa.ServeDeChao(cx, cy)) return RecusaDeAssento.Pode;

		// A ORDEM AQUI E A DA FRASE, e nao a do `ServeDeChao`: o jogador precisa ouvir o motivo mais
		// especifico. "Dentro de uma parede" dito em cima de um lago foi exatamente a queixa.
		if (mapa.EhAgua(cx, cy)) return RecusaDeAssento.EmCimaDagua;
		if (mapa.EhNuvem(cx, cy)) return RecusaDeAssento.EmCimaDeNuvem;
		if (mapa.NaBorda(cx, cy)) return RecusaDeAssento.BeiradaDoMapa;
		return RecusaDeAssento.DentroDeParede;
	}

	/// <summary>
	/// JA TEM COISA NESTE PEDACO DE CHAO?
	///
	/// A LISTA VEM DE QUEM PERGUNTA porque as duas pontas guardam as coisas em lugares diferentes --
	/// o servidor tem `_noChao` e as naves paradas em listas separadas, o cliente recebe as duas
	/// fundidas no mesmo pacote (ver `MandarObras`). O que NAO pode divergir e a FOLGA, e ela mora
	/// aqui.
	/// </summary>
	public static bool TemCoisaEm(IEnumerable<Vec2> ocupados, Vec2 alvo)
	{
		foreach (Vec2 o in ocupados)
			if (Math.Abs(o.X - alvo.X) < FolgaEntreObras && Math.Abs(o.Y - alvo.Y) < FolgaEntreObras)
				return true;
		return false;
	}

	/// <summary>
	/// A FRASE DA RECUSA -- uma so, pras duas pontas.
	///
	/// O cliente a diz ANTES de mandar (quando ele mesmo ja sabe que nao cabe, e ai nem sai pacote);
	/// o servidor a diz quando ele recusa algo que o cliente achou que cabia. Mesma palavra nos dois
	/// casos: pro jogador e a mesma situacao, e duas redacoes pra ela pareceriam dois problemas.
	/// </summary>
	public static string Motivo(RecusaDeAssento r, string nome) => r switch
	{
		RecusaDeAssento.NaoEDoChao => $"{nome} não é coisa de pôr no chão -- é de uso pessoal.",
		RecusaDeAssento.LongeDemais => "longe demais -- chegue mais perto do lugar.",
		RecusaDeAssento.DentroDeParede => "não dá pra assentar dentro de uma parede.",
		RecusaDeAssento.EmCimaDagua => "não dá pra assentar na água.",
		RecusaDeAssento.EmCimaDeNuvem => "não dá pra assentar em cima de nuvem.",
		RecusaDeAssento.BeiradaDoMapa => "é a beirada do mundo -- escolha um lugar mais pra dentro.",
		RecusaDeAssento.LugarOcupado => "já tem coisa demais neste ponto.",
		_ => "não deu pra assentar aí.",
	};
}
