using Godot;

namespace Jandirus.Client;

/// <summary>
/// ============================ QUEM SOBE QUANDO O CORPO VOA ============================
/// A pergunta que este arquivo responde nao e "quais nodes sobem". E o CONTRARIO: **todo filho
/// visual do corpo sobe, e quem NAO sobe tem que declarar isso em si mesmo**.
///
/// POR QUE A PERGUNTA TEVE QUE VIRAR DO AVESSO. Ate aqui a resposta morava numa lista escrita a
/// mao dentro de `LocalPlayer.AplicarAltura` (e na copia dela em `RemotePlayer.Altura`). A lista
/// esqueceu alguem QUATRO vezes seguidas, e o comentario que ela mesma carregava ja registrava
/// tres:
///   1. a aura ficava no chao com o dono no alto;
///   2. a barra de vida ficava plantada no chao, longe do dono (essa barra ja nao existe: foi
///      deletada a pedido do dono -- ver `EntityState` --, mas o defeito dela e historia que
///      explica esta regra);
///   3. a chama da carga desenhava no chao enquanto o corpo carregava Ki no ar;
///   4. a nebulosa do Ultra Instinto -- um quad MAIOR que o personagem -- ficava parada no chao,
///      e por ser grande nem parecia "efeito baixo": parecia um borrao branco solto no cenario.
///
/// Quatro vezes nao e descuido, e desenho: uma lista por NOME so cresce quando alguem lembra de
/// faze-la crescer, e o unico aviso de que ela ficou pra tras e o defeito na tela. O node novo
/// nasce em `World.AoEntrar` (ou no irmao dele, no ramo do corpo remoto) e nao ha nada, nem no
/// compilador nem na bancada, que ligue os dois arquivos.
///
/// COM A REGRA INVERTIDA, O NODE FUTURO ENTRA SOZINHO. Ele e filho do corpo; a varredura o
/// encontra por ser `Node2D`/`Control`, sem saber o nome nem o tipo dele; e ele sobe. Pra ficar
/// de fora, ele precisa dizer -- e dizer e escrever <see cref="IFicaNoChao"/> na declaracao da
/// PROPRIA classe, que e o unico lugar onde quem escreveu o node esta olhando.
///
/// O QUE ISTO **NAO** GENERALIZA. Subir e uniforme: todo mundo recebe o mesmo vetor. Ligar/apagar
/// por forma nao e -- cada node quer um argumento diferente do catalogo (`World.AplicarFormaVisual`
/// pergunta a folha pra aura, a cor e o volume pros raios, um booleano pra nebulosa). Aquela lista
/// continua a mao porque ali a lista E a informacao; aqui ela era so uma copia da arvore de nodes.
/// ======================================================================================
/// </summary>
public static class SubirComOVoo
{
	/// <summary>
	/// LEVANTA O DESENHO DOS FILHOS, e so o desenho.
	///
	/// ============================ O NODE DO CORPO FICA ONDE ESTA ============================
	/// O `corpo` e a posicao de verdade: e dele que saem a colisao, o alcance do soco, o Y-sort e a
	/// camera. Subir o node "pra parecer alto" moveria o corpo pra valer -- o jogador acertaria
	/// socos a 160 px de onde o servidor acha que ele esta (`Voo.AlturaMaxima` 640 x
	/// `Voo.EscalaNaTela` 0,25), e a briga com o servidor voltaria por um caminho novo. Ja custou
	/// caro neste projeto, na queixa de "a hitbox pega MUITO longe".
	///
	/// Por isso o deslocamento vai nos FILHOS, um por um, e nunca no pai.
	/// ====================================================================================
	///
	/// ============================ E ELE VAI NA GRADE DE DESENHO ============================
	/// `altitude * Voo.EscalaNaTela` (0,25) da um numero quebrado -- 137 px de altitude viram 34,25.
	/// Enquanto o motor arredondava sozinho (`snap_2d_transforms_to_pixel`) isso nao aparecia; com o
	/// arredondamento agora sendo nosso (ver `LocalPlayer.NoPontoDaGrade` e o porque no
	/// `project.godot`), um deslocamento quebrado poria o boneco -- e, subindo, o MUNDO INTEIRO --
	/// em subpixel.
	///
	/// O "mundo inteiro" nao e exagero: a `Camera2D` do corpo local e um destes filhos e sobe junto
	/// (ver o caso normal la embaixo). Sem esta linha, decolar deixaria o cenario deslizando em
	/// meio pixel enquanto a altura muda -- exatamente o defeito que este trabalho veio consertar,
	/// so que so no ar.
	/// ====================================================================================
	/// </summary>
	public static void Aplicar(Node2D corpo, Vector2 deslocamento)
	{
		deslocamento = LocalPlayer.NoPontoDaGrade(deslocamento, World.GradeDeDesenho);
		foreach (Node filho in corpo.GetChildren()) Levantar(filho, deslocamento);
	}

	/// <summary>
	/// DEFEITO INJETADO (bancada): o filho que nasce com o corpo JA parado no ar nao e levantado -- fica na
	/// altura do chao ate a altura do corpo mudar, como era antes de 2026-10-09. Falso em jogo, sempre.
	/// </summary>
	public static bool FilhoNovoFicaNoChaoDeTeste;

	/// <summary>
	/// UM FILHO NASCEU COM O CORPO JA NO AR: ele sobe AGORA, pelo mesmo vetor que os irmaos ja tem.
	///
	/// ============================ A VARREDURA SO RODA QUANDO A ALTURA MUDA ============================
	/// E e o certo pra ela (`LocalPlayer.AplicarAltura` e `RemotePlayer.Altura` saem cedo quando a altura e
	/// a mesma do quadro anterior: reescrever a posicao de dez nodes por quadro pra dizer o que ja estava
	/// dito). So que isso deixa um buraco do tamanho de um node: o filho pendurado DEPOIS da ultima mudanca
	/// nasce em `Position` zero -- no plano do chao -- e fica la ate o corpo subir ou descer de novo.
	///
	/// Foi a queixa do dono (2026-10-09): *"o efeito de carregar o beam e soltar ele, deve acompanhar o
	/// personagem n importa a altura q ele esteja voando"*. A bola da carga (`CargaDeRaioVisual`) e o caso
	/// em que o buraco NUNCA fecha sozinho: ela nasce quando o canal abre, e com um ataque de ki na mao a
	/// altura do corpo fica presa (`GameServer.AlturaPresaPeloKi`). Pairando a 20 tiles o brilho se juntava
	/// 160 px abaixo das maos, fora da tela. O jato de sangue de um membro arrancado no ar (o elo do
	/// `CombatFx.JatoDeSangue`) e a capsula de quem embarca voando caiam no mesmo buraco.
	///
	/// A `EsquivaZanzoken` ja tinha tapado o dela copiando a posicao do `Visual` no nascimento. E a QUINTA
	/// vez que um filho do corpo fica no chao (ver o cabecalho), e de novo por o conserto morar em quem
	/// lembra: aqui ele passa a morar no corpo, que avisa esta funcao de todo filho que entra
	/// (`Node.ChildEnteredTree`). O node futuro entra na conta sozinho, como na varredura.
	///
	/// NO CHAO NAO SE ESCREVE NADA, de proposito: com deslocamento zero o filho fica com a posicao em que
	/// nasceu, que e o que sempre aconteceu -- a varredura so a reescreve no dia em que o corpo decola.
	/// ==================================================================================================
	/// </summary>
	public static void AoNascer(Node filho, Vector2 deslocamento)
	{
		if (FilhoNovoFicaNoChaoDeTeste || deslocamento == Vector2.Zero) return;
		Levantar(filho, LocalPlayer.NoPontoDaGrade(deslocamento, World.GradeDeDesenho));
	}

	/// <summary>UM FILHO, um deslocamento ja na grade de desenho. E a regra inteira de quem sobe, e como.</summary>
	private static void Levantar(Node filho, Vector2 deslocamento)
	{
		// QUEM DECLAROU QUE FICA, FICA. Ver `IFicaNoChao` pra a lista dos dois casos legitimos
		// e pro motivo de cada um.
		if (filho is IFicaNoChao) return;

		switch (filho)
		{
			// O CANAL PROPRIO VEM PRIMEIRO. Node com altura propria sobre a cabeca (hoje so o
			// balao de fala) SOMA o deslocamento a ela; escrever `Position` cru apagava a
			// `AlturaBase` e derrubava o desenho da cabeca pro umbigo assim que a pessoa subia.
			// Isso nunca aparecia parado no chao, onde o deslocamento e zero e a conta da no mesmo
			// por acidente. Ver `ISobeComOCorpo`.
			case ISobeComOCorpo proprio:
				proprio.Deslocamento = deslocamento;
				break;

			// O CASO NORMAL, e ele cobre tipos que nem sao nossos: a `Camera2D` do corpo local cai
			// aqui. Ela PRECISA subir -- ela e filha do node, que fica na altura do chao, e sem
			// isso subir empurrava o personagem pra borda de cima da tela e depois pra fora dela
			// (o que ficava centralizado era a SOMBRA). Antes havia uma linha so pra ela, achando-a
			// por tipo; agora ela e so mais um filho, e nao ha linha nenhuma.
			//
			// Move-se a POSICAO da camera, e nao o `Offset`: o tremor de impacto ja escreve o
			// Offset todo quadro (ver `World._Process`), e dois donos pro mesmo campo e briga.
			case Node2D no2d:
				no2d.Position = deslocamento;
				break;

			// UM `Control` FILHO DE `Node2D` tambem desenha no espaco do pai, entao ele sobe pela
			// mesma razao. Nenhum existe hoje; a linha esta aqui porque o custo dela e zero e o
			// custo de descobrir que faltava seria mais um defeito na tela.
			case Control ctrl:
				ctrl.Position = deslocamento;
				break;
		}
	}
}

/// <summary>
/// "EU FICO NO CHAO MESMO COM O CORPO NO AR" -- a UNICA forma de escapar da subida.
///
/// So ha tres casos legitimos hoje, e os tres sao o mesmo motivo por caminhos diferentes: o node
/// nao desenha na altura do corpo.
///
///   * <see cref="SombraDeVoo"/> -- ela fica na origem DE PROPOSITO. O vao entre ela e o corpo E a
///     altura na tela; sombra que sobe junto vira uma segunda mancha grudada no pe e a altura deixa
///     de ser legivel. Ela recebe a altura por um canal proprio (`SombraDeVoo.Altura`).
///   * <see cref="MarcaDeAlvo"/> -- decalque de PISO (achatado, `ZIndex` -3). Ela mora ao lado da
///     sombra pelo mesmo motivo da sombra, e subir apagaria de quebra o `(0, 14)` que a poe aos pes.
///   * <see cref="RastroDeCorrida"/> -- ele nao desenha nada. As fotos que ele larga sao carimbadas
///     em `GlobalPosition` do DESENHO do corpo (que ja carrega a altitude) e ficam paradas no palco
///     enquanto o corpo segue. Mover este node nao mudaria pixel nenhum; a declaracao esta aqui pra
///     que a bancada saiba que ele ficar parado e a resposta CERTA, e nao um esquecimento.
///
/// PRA UM NODE NOVO: se voce esta pensando em escrever isto, o teste e "o que eu desenho fica no
/// chao quando o dono voa?". Se a resposta for nao, nao escreva -- deixe a regra normal agir.
/// </summary>
public interface IFicaNoChao { }

/// <summary>
/// "EU SUBO, MAS QUEM ESCREVE A MINHA POSICAO SOU EU".
///
/// Pra o node que ja tem uma altura propria em repouso (o balao de fala fica sobre a CABECA, nao
/// no centro do corpo). Receber `Position = deslocamento` cru apagaria essa altura.
/// O node implementa o canal e faz a SOMA -- que e onde a informacao de quanto ele senta acima da
/// cabeca ja mora.
/// </summary>
public interface ISobeComOCorpo
{
	/// <summary>O deslocamento de altitude, em pixels de tela (Y negativo = mais alto).</summary>
	Vector2 Deslocamento { set; }
}
