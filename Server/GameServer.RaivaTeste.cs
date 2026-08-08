using Godot;
using Jandirus.Core.Forms;

namespace Jandirus.Server;

/// <summary>
/// BANCADA AO VIVO DOS DOIS DEGRAUS DE RAIVA -- roda dentro do `--formasteste`.
///
/// ============================ O QUE SO DAQUI SE VE ============================
/// A bancada `raiva` (AssetPipeline) prova as REGRAS: quem pede `Extrema`, quem pede `Lendaria`, e
/// que o `EstadoDeForma.Avaliar` cobra as duas. Ela nao pode provar TRES coisas, e as tres sao
/// justamente onde este port ja quebrou antes:
///
///   1. **QUE O PERFIL DO JOGO CARREGA O CAMPO.** La o `PerfilDeFormas` e escrito a mao; aqui ele
///      sai do `Perfil(pl)`, e o valor tem que NASCER DO RELOGIO -- das duas janelas do
///      `ServerPlayer`. Apagar a linha `Raiva:` daquele construtor deixaria o Core inteiro verde e
///      destravaria o jogo todo, porque `Nenhuma` e o padrao do struct... e o padrao RECUSA. Ou
///      seja: o defeito seria o oposto -- tranca calada, forma que nunca vem, ninguem entende.
///   2. **QUE A TECLA C OBEDECE.** O jogador nao escolhe forma: ele aperta C e o servidor oferece
///      o degrau mais forte aberto (`Proxima`). Perguntar so ao `Avaliar` deixa de fora o unico
///      funil por onde a forma pode vazar em jogo.
///   3. **QUE A FRASE CERTA SAI PELA BOCA DO JOGO.** Sao dois precos, e a diferenca entre eles nao
///      tem numero nem barra na tela: ou esta na frase, ou o Legendary nunca descobre que o dele e
///      mais barato. Isso e pacote no fio, e so a escuta do servidor le.
/// ==========================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// OS DOIS DEGRAUS, NO CORPO VIVO. Guarda e repoe tudo o que mexe -- classe, forma, liberadas,
	/// maestrias e as duas janelas --, pelo mesmo motivo das outras secoes deste arquivo: o
	/// estranho de um bloco nao pode virar o resultado do seguinte.
	/// </summary>
	private void ADuplaRaivaAoVivo(ServerPlayer pl, Action<string, bool, string> Checa)
	{
		string classeAntes = pl.Ficha.Class, formaAntes = pl.Forma.Atual;
		var liberadasAntes = new HashSet<int>(pl.Forma.Liberadas);
		var estreiasAntes = new HashSet<int>(pl.Forma.EstreiaVista);
		long extremaAntes = pl.FuriaExtremaAte, lendariaAntes = pl.RaivaLendariaAte;
		double kiAntes = pl.Ficha.Ki;

		try
		{
			pl.Ficha.KO = false;
			pl.Ficha.dead = false;
			pl.Ficha.Ki = pl.Ficha.MaxKi;
			pl.FuriaExtremaAte = 0;
			pl.RaivaLendariaAte = 0;
			pl.Forma.Entrar(Catalogo.IdBase);
			AplicarForma(pl);

			// ============================ 1. AS DUAS JANELAS, LIDAS DO RELOGIO ============================
			Checa("em paz, o perfil do JOGO diz calma",
				  Perfil(pl).Raiva == NivelDeRaiva.Nenhuma, Perfil(pl).Raiva.ToString());

			Checa("o gancho com grau LENDARIO erupciona",
				  AmigoAbatido(pl, "Krillin", NivelDeRaiva.Lendaria), "");
			Checa("...e o perfil do jogo passa a dizer LENDARIA",
				  Perfil(pl).Raiva == NivelDeRaiva.Lendaria, Perfil(pl).Raiva.ToString());
			Checa("...e repetir o mesmo grau so prolonga (a cena nao toca duas vezes)",
				  !AmigoAbatido(pl, "Yamcha", NivelDeRaiva.Lendaria), "");

			// SUBIR DE GRAU ERUPCIONA DE NOVO, e tem que erupcionar: e uma dor NOVA, e a cinematica
			// de luto que um dia pendurarem aqui nao pode ser engolida por uma janela mais fraca ja
			// aberta. `jaEstava` compara o nivel EFETIVO com o grau que chegou -- e nao um booleano.
			Checa("subir de LENDARIA pra EXTREMA erupciona de novo",
				  AmigoAbatido(pl, "Bulma", NivelDeRaiva.Extrema), "");
			Checa("...e o perfil passa a dizer EXTREMA (a maior das duas janelas manda)",
				  Perfil(pl).Raiva == NivelDeRaiva.Extrema, Perfil(pl).Raiva.ToString());

			// E UM NOCAUTE NO MEIO DO LUTO NAO REBAIXA NINGUEM. Este e o defeito que os dois campos
			// separados existem pra impedir: com uma janela so, este `AmigoAbatido` sobrescreveria
			// o grau e fecharia o SSJ1 na cara de quem acabou de ver um amigo morrer.
			AmigoAbatido(pl, "Chichi", NivelDeRaiva.Lendaria);
			Checa("um nocaute no meio do luto NAO rebaixa a raiva",
				  Perfil(pl).Raiva == NivelDeRaiva.Extrema, Perfil(pl).Raiva.ToString());

			// E CALMA NAO E EVENTO. Um chamador distraido passando `Nenhuma` nao pode apagar janela
			// nenhuma -- senao o jeito de tirar a forma de alguem seria "abater um amigo com grau 0".
			Checa("acender `Nenhuma` nao faz nada e devolve FALSE",
				  !AmigoAbatido(pl, "ninguem", NivelDeRaiva.Nenhuma)
				  && Perfil(pl).Raiva == NivelDeRaiva.Extrema, Perfil(pl).Raiva.ToString());

			// AS JANELAS FECHAM SOZINHAS -- o prazo e puxado pra tras em vez de esperar 2 minutos.
			pl.FuriaExtremaAte -= (long)(SegundosDeRaiva * 1000) + 500;
			Checa("passado o prazo do luto, sobra a raiva LENDARIA (que ainda corre)",
				  Perfil(pl).Raiva == NivelDeRaiva.Lendaria, Perfil(pl).Raiva.ToString());
			pl.RaivaLendariaAte -= (long)(SegundosDeRaiva * 1000) + 500;
			Checa("passado o prazo das duas, volta a calma sem ninguem apagar nada",
				  Perfil(pl).Raiva == NivelDeRaiva.Nenhuma, Perfil(pl).Raiva.ToString());

			// ============================ 2. A TECLA C NUM SAIYAJIN COMUM ============================
			// O tronco pede a furia do LUTO. Com o desconto da linha Legendary aceso e mais nada, o
			// C nao pode sair da base -- e essa e a checagem que separa "dois degraus" de "um degrau
			// com dois nomes".
			pl.Ficha.Class = "Normal";
			pl.Forma.Entrar(Catalogo.IdBase);
			AplicarForma(pl);

			AmigoAbatido(pl, "Krillin", NivelDeRaiva.Lendaria);
			EscutaDeAvisos = [];
			SubirAteParar(pl);
			List<string> comDesconto = EscutaDeAvisos ?? [];
			EscutaDeAvisos = null;

			Checa("Saiyajin comum com raiva LENDARIA: a tecla C nao sai da base",
				  pl.Forma.NaBase, "chegou em " + pl.Forma.Atual);
			Checa("...e a recusa fala da DOR que ele ainda nao teve",
				  comDesconto.Any(a => a.Contains("dor", StringComparison.OrdinalIgnoreCase)),
				  string.Join(" | ", comDesconto));

			AmigoAbatido(pl, "Krillin", NivelDeRaiva.Extrema);
			pl.Ficha.Ki = pl.Ficha.MaxKi;
			SubirAteParar(pl);
			Checa("acesa a furia do LUTO, o MESMO C sobe a escada Saiyajin",
				  !pl.Forma.NaBase, pl.Forma.Atual);
			Checa("...e o corpo recebe o multiplicador da forma em que parou",
				  pl.Ficha.ssjBuff > 1.5, $"ssjBuff {pl.Ficha.ssjBuff:0.###}");

			// ============================ 3. A TECLA C NUM LEGENDARY ============================
			// O desconto do dono, medido: o MESMO nocaute que nao move um Saiyajin comum move a
			// linha Legendary inteira. Sem esta metade, "a raiva lendaria existe" seria uma frase
			// sobre um enum -- ela so vira regra quando alguem sobe com ela e o vizinho nao sobe.
			pl.Forma.Entrar(Catalogo.IdBase);
			pl.Forma.Liberadas.Clear();
			pl.Forma.EstreiaVista.Clear();
			pl.FuriaExtremaAte = 0;
			pl.RaivaLendariaAte = 0;
			pl.Ficha.Class = "Legendary";
			pl.Ficha.Ki = pl.Ficha.MaxKi;
			AplicarForma(pl);

			EscutaDeAvisos = [];
			SubirAteParar(pl);
			List<string> emPaz = EscutaDeAvisos ?? [];
			EscutaDeAvisos = null;
			Checa("Legendary em paz: a tecla C tambem nao sai da base",
				  pl.Forma.NaBase, "chegou em " + pl.Forma.Atual);
			Checa("...e a recusa dele fala de ver alguem CAIR, e nao da morte de um amigo",
				  emPaz.Any(a => a.Contains("cair", StringComparison.OrdinalIgnoreCase)),
				  string.Join(" | ", emPaz));

			AmigoAbatido(pl, "Krillin", NivelDeRaiva.Lendaria);
			pl.Ficha.Ki = pl.Ficha.MaxKi;
			SubirAteParar(pl);
			Checa("com o MESMO nocaute que nao moveu o Saiyajin comum, o Legendary sobe",
				  !pl.Forma.NaBase, pl.Forma.Atual);
			Checa("...e o degrau que saiu e da linha Legendary",
				  pl.Forma.Def?.Linha == LinhaDeForma.Legendary, pl.Forma.Def?.Linha.ToString() ?? "?");

			// E O LUTO TAMBEM SERVE PRA ELE -- o `>=` do passo 9, no corpo vivo. Com igualdade
			// estrita, um Legendary de luto ficaria preso na base sem entender por que.
			pl.Forma.Entrar(Catalogo.IdBase);
			pl.Forma.Liberadas.Clear();
			pl.RaivaLendariaAte = 0;
			pl.FuriaExtremaAte = 0;
			AmigoAbatido(pl, "Bulma", NivelDeRaiva.Extrema);
			pl.Ficha.Ki = pl.Ficha.MaxKi;
			SubirAteParar(pl);
			Checa("Legendary em LUTO tambem sobe (quem viu morrer viu cair)",
				  !pl.Forma.NaBase, pl.Forma.Atual);
		}
		finally
		{
			pl.Forma.Entrar(Catalogo.IdBase);
			pl.Forma.Liberadas.Clear();
			pl.Forma.Liberadas.UnionWith(liberadasAntes);
			pl.Forma.EstreiaVista.Clear();
			pl.Forma.EstreiaVista.UnionWith(estreiasAntes);
			pl.Ficha.Class = classeAntes;
			pl.FuriaExtremaAte = extremaAntes;
			pl.RaivaLendariaAte = lendariaAntes;
			pl.Ficha.Ki = kiAntes;
			if (formaAntes != Catalogo.IdBase) pl.Forma.Entrar(formaAntes);
			AplicarForma(pl);
			EscutaDeAvisos = null;
		}
	}

	/// <summary>
	/// APERTA C ATE PARAR DE SUBIR, passando as cinematicas.
	///
	/// PELO `Transformar` E NAO PELO `Avaliar`: e a MESMA funcao que a tecla C do jogador chama, e e
	/// o unico funil por onde uma forma pode vazar em jogo (`Proxima` escolhe o degrau mais forte
	/// aberto -- perguntar so ao `Avaliar` deixaria esse caminho de fora).
	///
	/// A CENA E QUEIMADA A CADA DEGRAU pelo <see cref="PassarACena"/> daqui do lado: enquanto ela
	/// prende o corpo, o proximo `Transformar` nao pega -- e a bancada concluiria "a escada parou"
	/// no meio de uma cinematica em vez de num gate.
	///
	/// TETO DE VOLTAS e nao `while`: um degrau que passasse a se repetir viraria laco infinito e a
	/// bancada TRAVARIA em vez de reprovar -- o unico jeito de um teste ser pior que nenhum.
	/// </summary>
	private void SubirAteParar(ServerPlayer pl)
	{
		for (int c = 0; c < 12; c++)
		{
			string antes = pl.Forma.Atual;
			pl.Ficha.Ki = pl.Ficha.MaxKi;
			Transformar(pl, subir: true);
			PassarACena(pl);
			if (pl.Forma.Atual == antes) return;
		}
		GD.PrintErr("[bancada] SubirAteParar bateu no teto de 12 degraus");
	}
}
