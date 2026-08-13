using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// UMA ESTRELA NO MUNDO -- e nao na carta.
///
/// ============================ ELA E O PRIMEIRO AVISO, E O UNICO QUE NAO E TEXTO ============================
/// A estrela passou a QUEIMAR (ver `Core.World.CalorDaEstrela`), e um corpo letal invisivel e um bug
/// do ponto de vista de quem joga -- a spec pede o sinal e o dono pediu que desse pra tentar sair.
/// Ate a fase 3 as estrelas so existiam na CARTA (`MapaEstelar`) e na tela do sistema
/// (`TelaDoSistema`): quem estivesse voando no espaco nao via nada, e derreteria olhando pro vazio.
///
/// A escala resolve isso sozinha: a menor estrela do catalogo tem 360 px de raio e a janela de mundo
/// mostra 384x216 px. Ou seja, a ana vermelha mais modesta enche a tela ANTES de o corpo encostar
/// nela, e a gigante azul cobre onze telas de diametro. Nao ha como nao ver.
/// ========================================================================================================
///
/// ============================ NENHUM BYTE NOVO NA REDE ============================
/// O servidor NAO manda estrelas. Ele ja manda a seed do universo no `S2C.Vizinhanca`, e a estrela e
/// funcao pura de (seed, celula) -- `Sistemas.EstrelaPerto`. Cliente e servidor chegam a mesma
/// estrela sem trocar um byte, que e a regra 0.2 da especificacao e ja e como o `MapaEstelar` e a
/// `TelaDoSistema` funcionam.
///
/// **Isso nao e economia de banda, e correcao**: se a posicao viesse por pacote, a estrela desenhada
/// e a estrela que queima seriam DOIS numeros, e o dia em que divergissem o jogador morreria ao lado
/// de um sol que parecia estar longe.
/// ==============================================================================
///
/// O NUCLEO E O RAIO LETAL. A folha transborda o nucleo (coroa e halo), e a fracao foi medida folha
/// a folha -- ver <see cref="ArteDeEstrela.LadoDaFolha"/>. Copiar o `Scale = Raio*2/lado` do
/// <see cref="PlanetaDesenhado"/> desenharia o nucleo MENOR do que a parte que mata.
/// </summary>
public partial class EstrelaDesenhada : Node2D
{
	public Estrela Ficha;

	/// <summary>O nome que aparece sobre ela. Vem do sistema (`SistemaSolar.NomeDaEstrela`).</summary>
	public string Nome = "";

	public override void _Ready()
	{
		// ATRAS DOS PLANETAS (-60) E NA FRENTE DO CEU (-100). Uma estrela por tras de um mundo em
		// orbita e a leitura certa: o planeta e o que se pousa, e ele nao pode sumir no brilho.
		ZIndex = -80;

		float raio = Ficha.Raio;
		Texture2D? folha = ArteDeEstrela.Textura(Ficha);
		float lado = ArteDeEstrela.LadoDaFolha(Ficha, raio);

		if (folha != null && lado > 0)
			AddChild(new Sprite2D
			{
				Name = "Folha",
				Texture = folha,
				Scale = Vector2.One * (lado / folha.GetWidth()),
				TextureFilter = TextureFilterEnum.Linear,
			});
		else
			// SEM ARTE, UM DISCO -- e nao nada. A tabela de folhas pode faltar (`estrelas.json` nao
			// gerado), e uma estrela que queima e nao desenha e o pior dos dois mundos.
			AddChild(new DiscoDeEstrela { Raio = raio, Cor = ArteDeEstrela.Halo(Ficha) });

		// ============================ O ANEL E A LINHA ENTRE VIVER E QUEIMAR ============================
		// Mesmo papel que ele tem na tela do sistema (`TelaDoSistema.DesenharEstrela`), e aqui ele vale
		// mais: **a folha transborda o nucleo** (coroa e halo pintados chegam a 1,8x o raio letal), entao
		// o brilho sozinho ensinaria a distancia errada -- e ensinaria pro lado que custa vida, porque o
		// jogador que confia no desenho para de nadar cedo demais na saida.
		//
		// E ele e a prova visual permanente de que a arte casa com o raio: se um dia a fracao medida do
		// nucleo (`ArteDeEstrela`) e o raio se desencontrarem, da pra VER, sem bancada nenhuma.
		// ==========================================================================================
		AddChild(new AnelDoRaioLetal { Raio = raio });

		if (Nome.Length == 0) return;
		Label rotulo = Tema.Legenda(Nome, ArteDeEstrela.Halo(Ficha), 13);
		rotulo.Position = new Vector2(-120, -raio - 40);
		rotulo.Size = new Vector2(240, 20);
		rotulo.HorizontalAlignment = HorizontalAlignment.Center;
		AddChild(rotulo);
	}
}

/// <summary>
/// O ANEL DO RAIO LETAL, desenhado sobre a folha. Ver o porque em <see cref="EstrelaDesenhada"/>.
///
/// Fino e translucido de proposito: ele e uma referencia, nao um cerco. Uma linha grossa vermelha em
/// volta de um sol leria como interface de aviso e mataria a imagem.
/// </summary>
public partial class AnelDoRaioLetal : Node2D
{
	public float Raio = 100;

	public override void _Ready() => ZIndex = 1;   // sobre a folha, dentro do no da estrela

	public override void _Draw()
	{
		// O `pontos` cresce com o raio: um arco de 64 lados numa gigante de 2.048 px deixa cada
		// segmento com 200 px, e o "circulo" vira um poligono visivel.
		int pontos = Mathf.Clamp((int)(Raio / 4f), 64, 360);
		DrawArc(Vector2.Zero, Raio, 0, Mathf.Tau, pontos, new Color(Tema.Perigo, 0.35f), 2f);
	}
}

/// <summary>
/// O DISCO DE EMERGENCIA: o que se desenha quando a folha da estrela nao carregou.
///
/// Existe pela mesma razao que o `PlanetaDesenhado` cai num icone generico em vez de num quadrado
/// vazio -- so que aqui o preco de nao desenhar nada e o jogador morrer sem ver o que o matou.
/// </summary>
public partial class DiscoDeEstrela : Node2D
{
	public float Raio = 100;
	public Color Cor = Colors.Orange;

	public override void _Draw()
	{
		DrawCircle(Vector2.Zero, Raio, Cor with { A = 0.85f });
		DrawCircle(Vector2.Zero, Raio * 1.25f, Cor with { A = 0.20f });
	}
}
