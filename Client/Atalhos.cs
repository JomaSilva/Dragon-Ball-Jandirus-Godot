using Godot;

namespace Jandirus.Client;

/// <summary>
/// O DISPARO DAS TECLAS DO JOGADOR -- a tecla ligada a um verb ou a uma transformacao.
///
/// ============================ ELE NAO TEM CAMINHO PROPRIO ============================
/// A tecla de um verb chama a MESMA <see cref="Verbo.Acionar"/> que o botao do menu chama. Nao ha
/// um segundo `SendVerbo` escrito aqui, nao ha `case` novo, nao ha opcode novo: o que o botao manda
/// e o que a tecla manda, byte por byte, porque e literalmente a mesma linha de codigo.
///
/// Isso importa mais do que parece. Se a tecla montasse o proprio pacote, cada verb passaria a ter
/// duas escritas -- e a segunda envelheceria calada no primeiro verb que ganhasse um argumento novo.
/// O sintoma seria o pior possivel: o botao funcionando e a tecla mandando o pacote do mes passado.
/// =====================================================================================
///
/// ============================ A TRANSFORMACAO E O UNICO ALVO SEM BOTAO ============================
/// Nao ha botao de "virar Blue" pra imitar: a escada sobe pelo duplo toque no C, que pede DIRECAO e
/// nao forma. Entao a tecla de forma estreia um comando -- e ele nasce dentro do funil que ja
/// existe, e nao ao lado dele: o `case "forma"` do `GameServer.Verbos.cs` chama
/// `TransformarPara`, que passa pelo `EstadoDeForma.Avaliar` como qualquer subida, com as recusas
/// valendo e a mesma frase que o C diria. Ver `GameServer.Formas.TransformarPara`.
///
/// O canal e o `C2S.Verbo` de sempre (comando + argumento). Abrir opcode pra isto seria o erro que
/// a medicao previu: se precisou de opcode novo, provavelmente errou o caminho.
/// ============================================================================================
/// </summary>
public partial class Atalhos : Node
{
	/// <summary>
	/// `_UnhandledInput` e nao `_Input`: as telas abertas tem preferencia. Quem esta com a mochila
	/// na frente e aperta a tecla do Kamehameha esta mexendo na mochila.
	/// </summary>
	public override void _UnhandledInput(InputEvent evento)
	{
		// ============================ OS TRES PORTOES ============================
		// 1. DIGITANDO. A pergunta unica da casa (`Foco.Digitando`). Sem ela, escrever o nome de
		//    quem vai ser banido dispararia uma tecnica por letra;
		// 2. A TELA DE TECLAS CAPTURANDO -- ja esta dentro do `Foco`, e e o caso mais bobo e mais
		//    certo de acontecer: ligar uma forma ao C transformaria voce durante a ligacao;
		// 3. O EMBATE. O `ClashQte` come QUALQUER letra de A a Z enquanto dura, e ele le em
		//    `_UnhandledInput` como este aqui -- `SetInputAsHandled` de um nao impede o outro de ter
		//    lido. Sem este portao, responder ao quick time event te transformaria.
		// =========================================================================
		if (Foco.Digitando) return;
		if (GameClient.Instance?.EmClash == true) return;
		if (evento is not InputEventKey { Pressed: true, Echo: false } k) return;

		if (Teclas.AtalhoDe(Teclas.Fisica(k)) is not { } a) return;
		GetViewport().SetInputAsHandled();
		Disparar(a);
	}

	/// <summary>
	/// ============================ O QUE ACONTECE QUANDO O ALVO NAO EXISTE ============================
	/// A ligacao e de MAQUINA (`config.json`) e o catalogo e do PERSONAGEM. Quem tinha um
	/// Namekuseijin com o Regenerar no Q e entra com um Saiyajin continua com o Q ligado, e tres
	/// coisas tem que ser verdade ao mesmo tempo:
	///
	///   * a ligacao **nao se apaga** -- apagar faria quem volta pro Namekuseijin perder o Q pra
	///     sempre, por ter trocado de personagem uma vez;
	///   * a tecla **nao manda pacote** -- um `SendHabilidade("regenerar")` de Saiyajin e um pacote
	///     por aperto pra ouvir nao;
	///   * a tecla **avisa**. Tecla que nao faz nada e indistinguivel de tecla quebrada, e o dono
	///     pediu isto com todas as letras.
	/// =============================================================================================
	/// </summary>
	private static void Disparar(Atalho a)
	{
		if (a.Tipo == "forma") { DispararForma(a); return; }

		Verbo? v = Verbos.PorChave(a.Chave);
		if (v == null)
		{
			Chat.Sistema($"\"{a.Rotulo}\": este personagem nao tem essa acao.");
			return;
		}

		// SEM ACAO = a faixa passiva de uma disciplina. Ela nem chega a ser oferecida pela tela
		// (ver `TelaDeTeclas.Ligaveis`), mas uma ligacao velha pode apontar pra uma.
		if (v.Acionar == null) { Chat.Sistema($"\"{v.Nome}\" nao e uma acao -- e uma passiva."); return; }

		// A MESMA RECUSA DO BOTAO. `PodeAgora` falso e o botao APAGADO no menu: ele nao manda
		// pacote nenhum, e a tecla nao pode mandar tambem. A diferenca e que a tecla FALA -- o
		// botao apagado se explica sozinho porque esta na tela, e a tecla nao esta.
		if (!v.PodeAgora) { Chat.Sistema($"\"{v.Nome}\" nao da agora."); return; }

		v.Acionar();
	}

	private static void DispararForma(Atalho a)
	{
		// O MESMO PORTAO DA TELA. Uma forma que este corpo nao desperta nao vira pacote -- ver
		// `FormasDespertas`. O servidor recusaria de qualquer jeito (o `Avaliar` e quem manda), mas
		// mandar pra ouvir nao a cada aperto e trafego por nada, e a frase daqui e mais util: ela
		// diz que o problema e o PERSONAGEM, e nao a forma.
		if (!FormasDespertas.Sei(a.Chave))
		{
			Chat.Sistema($"{a.Rotulo}: este personagem nao desperta essa forma.");
			return;
		}

		GameClient.Instance?.SendVerbo("forma", a.Chave);
	}
}
